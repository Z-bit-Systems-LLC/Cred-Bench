using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using CredBench.Core.Services.Leaf;
using NUnit.Framework;

namespace CredBench.Core.Tests.Leaf;

[TestFixture]
public class LeafTrustStoreTests
{
    private static X509Certificate2 CreateRoot(string cn, ECCurve curve)
    {
        using var key = ECDsa.Create(curve);
        var request = new CertificateRequest($"O=Z-bit Systems, CN={cn}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    [Test]
    public void New_HoldsOnlyNxpRoot()
    {
        var store = new LeafTrustStore();

        Assert.That(store.Anchors, Is.EqualTo(new[] { LeafTrustStore.NxpRootCaE200 }));
        Assert.That(store.Anchors[0].IsBuiltIn, Is.True);
        Assert.That(store.TestRoots, Is.Empty);
    }

    [Test]
    public void NxpRoot_IsAValidP256Point()
    {
        Assert.DoesNotThrow(() => LeafTrustStore.NxpRootCaE200.CreateVerifier().Dispose());
    }

    [Test]
    public void AddTestRoot_Pem_NamedAfterCnAndAppended()
    {
        using var root = CreateRoot("Bench Test Root", ECCurve.NamedCurves.nistP256);
        var store = new LeafTrustStore();
        var changed = 0;
        store.Changed += (_, _) => changed++;

        var anchor = store.AddTestRoot(Encoding.ASCII.GetBytes(root.ExportCertificatePem()));

        Assert.That(anchor.Name, Is.EqualTo("Bench Test Root"));
        Assert.That(anchor.IsBuiltIn, Is.False);
        Assert.That(anchor.PublicKey, Has.Length.EqualTo(65));
        Assert.That(anchor.CertificatePem, Does.StartWith("-----BEGIN CERTIFICATE-----"));
        Assert.That(store.Anchors, Has.Count.EqualTo(2));
        Assert.That(store.Anchors[1], Is.EqualTo(anchor));
        Assert.That(changed, Is.EqualTo(1));
    }

    [Test]
    public void AddTestRoot_Der_Accepted()
    {
        using var root = CreateRoot("DER Root", ECCurve.NamedCurves.nistP256);
        var store = new LeafTrustStore();

        var anchor = store.AddTestRoot(root.RawData);

        Assert.That(anchor.Name, Is.EqualTo("DER Root"));
    }

    [Test]
    public void AddTestRoot_SameKeyTwice_AddedOnce()
    {
        using var root = CreateRoot("Twice", ECCurve.NamedCurves.nistP256);
        var store = new LeafTrustStore();

        store.AddTestRoot(root.RawData);
        store.AddTestRoot(Encoding.ASCII.GetBytes(root.ExportCertificatePem()));

        Assert.That(store.TestRoots, Has.Count.EqualTo(1));
    }

    [Test]
    public void AddTestRoot_P384_Rejected()
    {
        using var root = CreateRoot("P-384", ECCurve.NamedCurves.nistP384);
        var store = new LeafTrustStore();

        Assert.Throws<ArgumentException>(() => store.AddTestRoot(root.RawData));
    }

    [Test]
    public void AddTestRoot_Garbage_Rejected()
    {
        Assert.Throws<ArgumentException>(() => new LeafTrustStore().AddTestRoot([1, 2, 3]));
    }

    [Test]
    public void RemoveTestRoot_RemovesOnlyTestRoots()
    {
        using var root = CreateRoot("Remove Me", ECCurve.NamedCurves.nistP256);
        var store = new LeafTrustStore();
        var anchor = store.AddTestRoot(root.RawData);

        Assert.That(store.RemoveTestRoot(LeafTrustStore.NxpRootCaE200), Is.False);
        Assert.That(store.RemoveTestRoot(anchor), Is.True);
        Assert.That(store.Anchors, Is.EqualTo(new[] { LeafTrustStore.NxpRootCaE200 }));
    }

    [Test]
    public void AddedTestRoot_VerifiesCertificatesItIssued()
    {
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootRequest = new CertificateRequest("CN=Issuing Root", rootKey, HashAlgorithmName.SHA256);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        using var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        using var cardKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var subject = new X500DistinguishedNameBuilder();
        subject.Add("2.5.4.5", "000000012345", System.Formats.Asn1.UniversalTagNumber.PrintableString);
        var cardRequest = new CertificateRequest(subject.Build(), cardKey, HashAlgorithmName.SHA256);
        using var card = cardRequest.Create(root, DateTimeOffset.UtcNow.AddDays(-1), root.NotAfter, [0x42]);

        var store = new LeafTrustStore();
        var anchor = store.AddTestRoot(root.RawData);
        var parsed = LeafCertificate.Parse(card.RawData);

        Assert.That(parsed.ProfileIssues, Is.Empty);
        Assert.That(parsed.OpenId, Is.EqualTo(12345));
        Assert.That(parsed.IsSignedBy(anchor), Is.True);
    }
}
