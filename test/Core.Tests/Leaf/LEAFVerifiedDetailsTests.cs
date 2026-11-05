using CredBench.Core.Models.TechnologyDetails;
using NUnit.Framework;

namespace CredBench.Core.Tests.Leaf;

[TestFixture]
public class LEAFVerifiedDetailsTests
{
    // Vectors from the LEAF Verified onboarding guide
    [TestCase(160000000000L, "4A817C8000")]
    [TestCase(1L, "0000000002")]
    [TestCase(274877906943L, "FFFFFFFFFE")]
    public void W40Hex_MatchesGuideVectors(long openId, string expected)
    {
        var details = new LEAFVerifiedDetails { OpenId = openId };

        Assert.That(details.W40Hex, Is.EqualTo(expected));
        Assert.That(details.W40Bits, Has.Length.EqualTo(40));
        Assert.That(Convert.ToInt64(details.W40Bits, 2).ToString("X10"), Is.EqualTo(expected));
    }

    [Test]
    public void W40_OutOfRange_IsNull()
    {
        var details = new LEAFVerifiedDetails { OpenId = LEAFVerifiedDetails.OpenIdMax + 1 };

        Assert.That(details.W40Hex, Is.Null);
        Assert.That(details.W40Bits, Is.Null);
        Assert.Throws<ArgumentOutOfRangeException>(() => LEAFVerifiedDetails.ToW40(LEAFVerifiedDetails.OpenIdMax + 1));
    }

    [Test]
    public void IsFullyVerified_RequiresBothSignatures()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new LEAFVerifiedDetails { Trust = LeafTrust.Verified, CardSignatureValid = true }.IsFullyVerified, Is.True);
            Assert.That(new LEAFVerifiedDetails { Trust = LeafTrust.Verified, CardSignatureValid = null }.IsFullyVerified, Is.False);
            Assert.That(new LEAFVerifiedDetails { Trust = LeafTrust.UntrustedIssuer, CardSignatureValid = true }.IsFullyVerified, Is.False);
        });
    }

    [Test]
    public void GetFields_Verified_ListsCredential()
    {
        var details = new LEAFVerifiedDetails
        {
            OpenId = 160000000000,
            Trust = LeafTrust.Verified,
            TrustAnchorName = "NXP LEAF RootCAvE200",
            CardSignatureValid = true,
            Issuer = "CN=Issuer"
        };

        var fields = details.GetFields().ToDictionary(f => f.Label, f => f.Value);

        Assert.That(fields["Status"], Is.EqualTo("Verified"));
        Assert.That(fields["Open ID"], Is.EqualTo("160000000000"));
        Assert.That(fields["W40-2 Hex"], Is.EqualTo("4A817C8000"));
        Assert.That(fields["Trust Anchor"], Is.EqualTo("NXP LEAF RootCAvE200"));
        Assert.That(fields["Card Signature"], Is.EqualTo("Valid"));
        Assert.That(fields.ContainsKey("Error"), Is.False);
    }

    [Test]
    public void GetFields_CloneSuspect_SaysSo()
    {
        var details = new LEAFVerifiedDetails { Trust = LeafTrust.Verified, CardSignatureValid = false };

        Assert.That(details.GetFields()[0].Value, Does.Contain("possible clone"));
    }

    [Test]
    public void GetFields_ListsEveryProfileIssueAndError()
    {
        var details = new LEAFVerifiedDetails
        {
            Trust = LeafTrust.ProfileViolation,
            ProfileIssues = ["one", "two"],
            Error = "boom",
            LastStatusWord = "91BE"
        };

        var fields = details.GetFields();

        Assert.That(fields.Count(f => f.Label == "Profile Issue"), Is.EqualTo(2));
        Assert.That(fields, Has.Some.EqualTo(("Error", "boom")));
        Assert.That(fields, Has.Some.EqualTo(("Status Word", "91BE")));
    }
}
