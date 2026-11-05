using CredBench.Core.Services.Leaf;
using NUnit.Framework;

namespace CredBench.Core.Tests.Leaf;

[TestFixture]
public class LeafCertificateTests
{
    private static readonly LeafTrustAnchor TestCa = new("Test CA", LeafVectors.CaPub, IsBuiltIn: false);

    private static LeafCertificate ParseMutated(int offset, byte value)
    {
        var cert = LeafVectors.CardCert.ToArray();
        cert[offset] = value;
        return LeafCertificate.Parse(cert);
    }

    [Test]
    public void Parse_Vector_MeetsProfile()
    {
        var cert = LeafCertificate.Parse(LeafVectors.CardCert);

        Assert.That(cert.ProfileIssues, Is.Empty);
        Assert.That(cert.OpenId, Is.EqualTo(LeafVectors.OpenId));
        Assert.That(cert.PublicKey, Is.EqualTo(LeafVectors.CardPub));
        Assert.That(cert.SerialHex, Is.EqualTo("1A2B3C4D5E6F"));
        Assert.That(cert.Issuer, Does.Contain("CN=AsymCred LEAF Test CA"));
        Assert.That(cert.AuthorityKeyId, Is.EqualTo("BEE3F2FCAED95EF73543EF797C70671EC59DDC3F"));
    }

    [Test]
    public void IsSignedBy_TestCa_True()
    {
        Assert.That(LeafCertificate.Parse(LeafVectors.CardCert).IsSignedBy(TestCa), Is.True);
    }

    [Test]
    public void IsSignedBy_NxpRoot_False()
    {
        Assert.That(LeafCertificate.Parse(LeafVectors.CardCert).IsSignedBy(LeafTrustStore.NxpRootCaE200), Is.False);
    }

    [Test]
    public void IsSignedBy_TamperedTbs_False()
    {
        // Change a byte inside the issuer name: still parses, no longer verifies
        Assert.That(ParseMutated(60, (byte)'z').IsSignedBy(TestCa), Is.False);
    }

    [Test]
    public void Parse_InnerSha384_ReportsInnerAlgorithm()
    {
        var cert = ParseMutated(LeafVectors.OffInnerSigAlgLast, 0x03);

        Assert.That(cert.ProfileIssues, Has.Some.Contains("Inner signature algorithm"));
    }

    [Test]
    public void Parse_OuterSha384_ReportsOuterAlgorithm()
    {
        var cert = ParseMutated(LeafVectors.OffOuterSigAlgLast, 0x03);

        Assert.That(cert.ProfileIssues, Has.Some.StartsWith("Signature algorithm"));
    }

    [Test]
    public void Parse_WrongCurve_ReportsKeyAndHasNoKey()
    {
        var cert = ParseMutated(LeafVectors.OffCurveOidLast, 0x08);

        Assert.That(cert.ProfileIssues, Has.Some.Contains("not P-256"));
        Assert.That(cert.PublicKey, Is.Null);
    }

    [Test]
    public void Parse_NoSerialNumberAttribute_ReportsMissingOpenId()
    {
        var cert = ParseMutated(LeafVectors.OffSerialOidLast, 0x06);

        Assert.That(cert.OpenId, Is.Null);
        Assert.That(cert.ProfileIssues, Has.Some.Contains("No subject serialNumber"));
    }

    [Test]
    public void Parse_SeveralViolations_ReportsAll()
    {
        var bytes = LeafVectors.CardCert.ToArray();
        bytes[LeafVectors.OffInnerSigAlgLast] = 0x03;
        bytes[LeafVectors.OffCurveOidLast] = 0x08;
        bytes[LeafVectors.OffOpenIdDigits] = (byte)'x';

        var cert = LeafCertificate.Parse(bytes);

        Assert.That(cert.ProfileIssues, Has.Count.EqualTo(3));
    }

    [Test]
    public void Parse_Truncated_Throws()
    {
        Assert.Throws<FormatException>(() => LeafCertificate.Parse(LeafVectors.CardCert[..200]));
    }
}
