using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CredBench.Core.Models.TechnologyDetails;
using CredBench.Core.Services.CardDetectors;
using CredBench.Core.Services.GlobalPlatform;
using CredBench.Core.Services.Leaf;
using NUnit.Framework;

namespace CredBench.Core.Tests.Leaf;

[TestFixture]
public class LeafProgrammingTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Path.Combine(Path.GetTempPath(), "credbench-leaf-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    private static IReadOnlyList<LeafTrustAnchor> AnchorsFor(LeafTestCa ca) =>
        [LeafTrustStore.ParseRootCertificate(ca.Certificate.RawData)];

    [Test]
    public void Create_RootIsAcceptedAsTrustAnchor()
    {
        using var ca = LeafTestCa.Create("Bench Root");

        var anchor = LeafTrustStore.ParseRootCertificate(ca.Certificate.RawData);

        Assert.That(anchor.Name, Is.EqualTo("Bench Root"));
        Assert.That(ca.Certificate.Subject, Is.EqualTo("CN=Bench Root, O=Z-bit Systems (TEST ONLY)"));
        Assert.That(ca.PublicKeyPoint, Has.Length.EqualTo(65));
    }

    [Test]
    public void DefaultRoot_SaysItIsFake_AndCardCertificatesCarryIt()
    {
        using var ca = LeafTestCa.Create();
        using var card = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var q = card.ExportParameters(false).Q;

        var details = LEAFVerifiedDetector.EvaluateCertificate(
            ca.IssueCardCertificate([0x04, .. q.X!, .. q.Y!], 1), AnchorsFor(ca));

        Assert.That(ca.Name, Is.EqualTo("FAKE LEAF Test Root - NOT FOR PRODUCTION"));
        Assert.That(LeafTrustStore.ParseRootCertificate(ca.Certificate.RawData).Name, Does.StartWith("FAKE"));
        Assert.That(details.Issuer, Does.Contain("FAKE LEAF Test Root - NOT FOR PRODUCTION").And.Contain("TEST ONLY"));
    }

    [TestCase(0L)]
    [TestCase(123456789L)]
    [TestCase(LEAFVerifiedDetails.OpenIdMax)]
    public void IssuedCertificate_PassesTheLeafProfileAndVerifies(long openId)
    {
        using var ca = LeafTestCa.Create();
        using var card = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var q = card.ExportParameters(false).Q;

        var certificate = ca.IssueCardCertificate([0x04, .. q.X!, .. q.Y!], openId);
        var details = LEAFVerifiedDetector.EvaluateCertificate(certificate, AnchorsFor(ca));

        Assert.Multiple(() =>
        {
            Assert.That(details.ProfileIssues, Is.Empty);
            Assert.That(details.Trust, Is.EqualTo(LeafTrust.Verified));
            Assert.That(details.OpenId, Is.EqualTo(openId));
            Assert.That(details.Subject, Is.EqualTo($"SERIALNUMBER={openId:D12}"));
        });
    }

    [Test]
    public void IssuedCertificate_MatchesLeafToolShape()
    {
        using var ca = LeafTestCa.Create();
        using var card = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var q = card.ExportParameters(false).Q;

        using var certificate = X509CertificateLoader.LoadCertificate(
            ca.IssueCardCertificate([0x04, .. q.X!, .. q.Y!], 42));

        var basic = certificate.Extensions.OfType<X509BasicConstraintsExtension>().Single();
        var usage = certificate.Extensions.OfType<X509KeyUsageExtension>().Single();
        var aki = certificate.Extensions.OfType<X509AuthorityKeyIdentifierExtension>().Single();
        var ski = ca.Certificate.Extensions.OfType<X509SubjectKeyIdentifierExtension>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(basic.Critical && !basic.CertificateAuthority, Is.True);
            Assert.That(usage.Critical && usage.KeyUsages == X509KeyUsageFlags.DigitalSignature, Is.True);
            Assert.That(aki.KeyIdentifier!.Value.ToArray(), Is.EqualTo(Convert.FromHexString(ski.SubjectKeyIdentifier!)));
            // leaf-tool's key identifier is SHA-1 of the root's point
            Assert.That(ski.SubjectKeyIdentifier, Is.EqualTo(Convert.ToHexString(SHA1.HashData(ca.PublicKeyPoint))));
            Assert.That(certificate.NotAfter.ToUniversalTime().Year, Is.EqualTo(9999));
            Assert.That(certificate.SignatureAlgorithm.Value, Is.EqualTo("1.2.840.10045.4.3.2"));
        });
    }

    [TestCase(-1L)]
    [TestCase(LEAFVerifiedDetails.OpenIdMax + 1)]
    public void Issue_OpenIdOutOfRange_Throws(long openId)
    {
        using var ca = LeafTestCa.Create();
        using var card = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var q = card.ExportParameters(false).Q;

        Assert.Throws<ArgumentOutOfRangeException>(() => ca.IssueCardCertificate([0x04, .. q.X!, .. q.Y!], openId));
    }

    [Test]
    public void Issue_PointNotOnCurve_Throws()
    {
        using var ca = LeafTestCa.Create();
        byte[] point = [0x04, .. new byte[64]];
        point[64] = 1;

        Assert.Throws<ArgumentException>(() => ca.IssueCardCertificate(point, 1));
    }

    [Test]
    public void SaveAndLoad_RoundTrip_IssuesUnderTheSameRoot()
    {
        using var original = LeafTestCa.Create("Round Trip");
        original.Save(_directory);

        using var loaded = LeafTestCa.Load(_directory);
        using var card = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var q = card.ExportParameters(false).Q;
        var certificate = loaded.IssueCardCertificate([0x04, .. q.X!, .. q.Y!], 7);

        Assert.That(loaded.Name, Is.EqualTo("Round Trip"));
        Assert.That(File.ReadAllText(Path.Combine(_directory, LeafTestCa.KeyFileName)), Does.StartWith("-----BEGIN PRIVATE KEY-----"));
        Assert.That(LEAFVerifiedDetector.EvaluateCertificate(certificate, AnchorsFor(original)).Trust, Is.EqualTo(LeafTrust.Verified));
    }

    [Test]
    public void Save_RefusesToOverwrite()
    {
        using var first = LeafTestCa.Create();
        using var second = LeafTestCa.Create();
        first.Save(_directory);

        Assert.Throws<IOException>(() => second.Save(_directory));
        using var loaded = LeafTestCa.Load(_directory);
        Assert.That(loaded.PublicKeyPoint, Is.EqualTo(first.PublicKeyPoint));
    }

    [Test]
    public void Load_KeyFromAnotherRoot_Throws()
    {
        using var first = LeafTestCa.Create();
        using var second = LeafTestCa.Create();
        first.Save(_directory);
        var other = Path.Combine(_directory, "other");
        second.Save(other);
        File.Copy(Path.Combine(other, LeafTestCa.KeyFileName), Path.Combine(_directory, LeafTestCa.KeyFileName), true);

        Assert.Throws<ArgumentException>(() => LeafTestCa.Load(_directory));
    }

    [Test]
    public void Load_MissingFiles_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => LeafTestCa.Load(_directory));
    }

    [Test]
    public void Personalise_ThenReadBack_IsFullyVerified()
    {
        using var ca = LeafTestCa.Create();
        using var applet = new FakeLeafApplet();

        var (cardKey, certificate) = LeafCardProgrammer.Personalise(applet, ca, 123456789);
        var readBack = LeafCardProgrammer.ReadBack(applet, ca);

        Assert.Multiple(() =>
        {
            Assert.That(applet.Locked, Is.True);
            Assert.That(cardKey, Is.EqualTo(applet.PublicKey));
            Assert.That(certificate.Length, Is.GreaterThan(128), "spans more than one PUT CERTIFICATE");
            Assert.That(readBack.IsFullyVerified, Is.True, readBack.Error);
            Assert.That(readBack.OpenId, Is.EqualTo(123456789));
            Assert.That(readBack.Certificate, Is.EqualTo(certificate));
        });
    }

    [Test]
    public void Personalise_AlreadyLocked_ExplainsReinstall()
    {
        using var ca = LeafTestCa.Create();
        using var applet = new FakeLeafApplet();
        LeafCardProgrammer.Personalise(applet, ca, 1);

        var ex = Assert.Throws<GlobalPlatformException>(() => LeafCardProgrammer.Personalise(applet, ca, 2));
        Assert.That(ex!.Message, Does.Contain("6985").And.Contain("locked"));
    }

    [Test]
    public void Personalise_LockRejected_Throws()
    {
        using var ca = LeafTestCa.Create();
        using var applet = new FakeLeafApplet { RejectLock = true };

        var ex = Assert.Throws<GlobalPlatformException>(() => LeafCardProgrammer.Personalise(applet, ca, 1));
        Assert.That(ex!.Message, Does.StartWith("LOCK failed: SW 6984"));
    }

    [Test]
    public void ReadBack_UnderAnotherRoot_IsUntrusted()
    {
        using var ca = LeafTestCa.Create();
        using var other = LeafTestCa.Create();
        using var applet = new FakeLeafApplet();
        LeafCardProgrammer.Personalise(applet, ca, 5);

        var readBack = LeafCardProgrammer.ReadBack(applet, other);

        Assert.That(readBack.Trust, Is.EqualTo(LeafTrust.UntrustedIssuer));
        Assert.That(readBack.CardSignatureValid, Is.True);
    }
}
