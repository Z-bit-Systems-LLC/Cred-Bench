using CredBench.Core.Models;
using CredBench.Core.Models.TechnologyDetails;
using CredBench.Core.Services.CardDetectors;
using CredBench.Core.Services.Leaf;
using NUnit.Framework;

namespace CredBench.Core.Tests.Leaf;

[TestFixture]
public class LEAFVerifiedDetectorTests
{
    private static readonly LeafTrustAnchor TestCa = new("AsymCred LEAF Test CA", LeafVectors.CaPub, IsBuiltIn: false);

    private sealed class FixedTrustStore(params LeafTrustAnchor[] anchors) : ILeafTrustStore
    {
        public IReadOnlyList<LeafTrustAnchor> Anchors { get; } = anchors;
    }

    private static LEAFVerifiedDetector CreateDetector(params LeafTrustAnchor[] anchors) =>
        new(new FixedTrustStore(anchors), () => LeafVectors.RndA.ToArray());

    private static LEAFVerifiedDetails Detect(FakeLeafCard card, params LeafTrustAnchor[] anchors)
    {
        var (detected, _, typed) = CreateDetector(anchors).Detect(card);
        Assert.That(detected, Is.True);
        return (LEAFVerifiedDetails)typed!;
    }

    [Test]
    public void Technology_ReturnsLEAFVerified()
    {
        Assert.That(CreateDetector().Technology, Is.EqualTo(CardTechnology.LEAFVerified));
    }

    [Test]
    public void Detect_TrustedCard_IsFullyVerified()
    {
        var card = new FakeLeafCard();

        var (detected, summary, typed) = CreateDetector(LeafTrustStore.NxpRootCaE200, TestCa).Detect(card);
        var details = (LEAFVerifiedDetails)typed!;

        Assert.That(detected, Is.True);
        Assert.That(summary, Is.EqualTo("LEAF Verified (Open ID 160000000000)"));
        Assert.That(details.OpenId, Is.EqualTo(LeafVectors.OpenId));
        Assert.That(details.W40Hex, Is.EqualTo("4A817C8000"));
        Assert.That(details.Trust, Is.EqualTo(LeafTrust.Verified));
        Assert.That(details.TrustAnchorName, Is.EqualTo("AsymCred LEAF Test CA"));
        Assert.That(details.CardSignatureValid, Is.True);
        Assert.That(details.IsFullyVerified, Is.True);
        Assert.That(details.CardPublicKeyHex, Is.EqualTo(Convert.ToHexString(LeafVectors.CardPub)));
        Assert.That(details.Certificate, Is.EqualTo(LeafVectors.CardCert));
        Assert.That(details.Issuer, Does.Contain("AsymCred LEAF Test CA"));
        Assert.That(details.ProfileIssues, Is.Empty);
        Assert.That(details.Error, Is.Null);
    }

    [Test]
    public void Detect_SendsExactCommands()
    {
        var card = new FakeLeafCard();

        Detect(card, TestCa);

        var hex = card.Commands.Select(Convert.ToHexString).ToList();
        Assert.That(hex[0], Is.EqualTo("905A000003D61CF500"));
        Assert.That(hex[1], Is.EqualTo("90AD0000070200000008000000"));
        var authenticate = card.Commands.Single(c => c[1] == 0x88);
        Assert.That(Convert.ToHexString(authenticate),
            Is.EqualTo("00880000168000" + "7C128110" + Convert.ToHexString(LeafVectors.RndA) + "00"));
        Assert.That(authenticate[4], Is.EqualTo(0x16), "Lc must be 0x16, not the 0x14 in the guide's prose");
        Assert.That(hex[^1], Is.EqualTo("905A000003D51CF500"), "The NDEF application is selected last");
    }

    [Test]
    public void Detect_ReadsCertificateInHeaderThen255ThenRemainder()
    {
        var card = new FakeLeafCard();

        Detect(card, TestCa);

        // 407-byte certificate: 8, then 255 at offset 8, then 144 at offset 263
        var reads = card.ReadDataCommands
            .Select(c => (Offset: c[6] | (c[7] << 8) | (c[8] << 16), Length: c[9] | (c[10] << 8) | (c[11] << 16)))
            .ToList();
        Assert.That(reads, Is.EqualTo(new[] { (0, 8), (8, 255), (263, 144) }));
        Assert.That(card.Commands.Count(c => c[0] == 0x90 && c[1] == 0xAF), Is.GreaterThan(0),
            "Continuation frames must be followed with 90 AF");
    }

    [Test]
    public void Detect_AcceptsIsoSuccessOnSelect()
    {
        var card = new FakeLeafCard { SelectResponse = [0x90, 0x00] };

        Assert.That(Detect(card, TestCa).IsFullyVerified, Is.True);
    }

    [Test]
    public void Detect_ApplicationNotFound_ReturnsNotDetected()
    {
        var card = new FakeLeafCard { SelectResponse = [0x91, 0xA0] };

        var (detected, summary, typed) = CreateDetector(TestCa).Detect(card);

        Assert.That(detected, Is.False);
        Assert.That(summary, Is.Null);
        Assert.That(typed, Is.Null);
        Assert.That(card.Commands, Has.Count.EqualTo(1));
    }

    [Test]
    public void Detect_SelectWithData_ReturnsNotDetected()
    {
        // A response carrying data is not a DESFire SelectApplication answer
        var card = new FakeLeafCard { SelectResponse = [0x01, 0x91, 0x00] };

        Assert.That(CreateDetector(TestCa).Detect(card).Detected, Is.False);
    }

    [Test]
    public void Detect_UntrustedIssuer_StillAuthenticates()
    {
        var card = new FakeLeafCard();

        var details = Detect(card, LeafTrustStore.NxpRootCaE200);

        Assert.That(details.Trust, Is.EqualTo(LeafTrust.UntrustedIssuer));
        Assert.That(details.TrustAnchorName, Is.Null);
        Assert.That(details.OpenId, Is.EqualTo(LeafVectors.OpenId));
        Assert.That(details.CardSignatureValid, Is.True);
        Assert.That(details.IsFullyVerified, Is.False);
        Assert.That(card.Commands.Any(c => c[1] == 0x88), Is.True);
    }

    [Test]
    public void Detect_SecondAnchorMatches_ReportsSecondAnchorName()
    {
        var details = Detect(new FakeLeafCard(), LeafTrustStore.NxpRootCaE200, TestCa);

        Assert.That(details.TrustAnchorName, Is.EqualTo(TestCa.Name));
    }

    [Test]
    public void Detect_FlippedCardSignature_ReportsCardSignatureInvalid()
    {
        var signature = LeafVectors.CardSig.ToArray();
        signature[10] ^= 0x01;
        var card = new FakeLeafCard { CardSignature = signature };

        var details = Detect(card, TestCa);

        Assert.That(details.Trust, Is.EqualTo(LeafTrust.Verified));
        Assert.That(details.CardSignatureValid, Is.False);
        Assert.That(details.IsFullyVerified, Is.False);
    }

    [Test]
    public void Detect_WrongChallengeEcho_ReportsCardSignatureInvalid()
    {
        // The card signed someone else's RndB
        var card = new FakeLeafCard { RndB = new byte[16] };

        Assert.That(Detect(card, TestCa).CardSignatureValid, Is.False);
    }

    [Test]
    public void Detect_PaddedSignatureForm_ReportedAsUnsupported()
    {
        byte[] response = [0x7C, 0x58, 0x81, 0x10, .. LeafVectors.RndB, 0x82, 0x44, .. LeafVectors.CardSig, 0, 0, 0, 0, 0x90, 0x00];
        var card = new FakeLeafCard { AuthenticateResponse = response };

        var details = Detect(card, TestCa);

        Assert.That(details.CardSignatureValid, Is.Null);
        Assert.That(details.Error, Does.Contain("82 44"));
        Assert.That(details.Trust, Is.EqualTo(LeafTrust.Verified), "The certificate result is kept");
    }

    [Test]
    public void Detect_MissingSignatureTag_ReportedAsMalformed()
    {
        byte[] response = [0x7C, 0x54, 0x81, 0x10, .. LeafVectors.RndB, 0x83, 0x40, .. LeafVectors.CardSig, 0x90, 0x00];
        var card = new FakeLeafCard { AuthenticateResponse = response };

        var details = Detect(card, TestCa);

        Assert.That(details.CardSignatureValid, Is.Null);
        Assert.That(details.Error, Does.StartWith("Unexpected authenticate response"));
    }

    [Test]
    public void Detect_AuthenticateRefused_ReportsStatusWord()
    {
        var card = new FakeLeafCard { AuthenticateResponse = [0x91, 0xAE] };

        var details = Detect(card, TestCa);

        Assert.That(details.CardSignatureValid, Is.Null);
        Assert.That(details.LastStatusWord, Is.EqualTo("91AE"));
        Assert.That(details.Error, Does.Contain("authentication error"));
    }

    [Test]
    public void Detect_OverDelivery_IsUnreadable()
    {
        var card = new FakeLeafCard { OverDeliver = 1 };

        var details = Detect(card, TestCa);

        Assert.That(details.Trust, Is.EqualTo(LeafTrust.Unreadable));
        Assert.That(details.Error, Does.Contain("more than"));
        Assert.That(card.Commands.Any(c => c[1] == 0x88), Is.False);
    }

    [Test]
    public void Detect_ShortRead_IsUnreadable()
    {
        var card = new FakeLeafCard { UnderDeliver = 1 };

        var details = Detect(card, TestCa);

        Assert.That(details.Trust, Is.EqualTo(LeafTrust.Unreadable));
        Assert.That(details.Error, Does.Contain("7 of 8"));
    }

    [Test]
    public void Detect_BoundaryErrorMidRead_KeepsPartialCertificate()
    {
        var card = new FakeLeafCard { ReadError = (263, [0x91, 0xBE]) };

        var details = Detect(card, TestCa);

        Assert.That(details.Trust, Is.EqualTo(LeafTrust.Unreadable));
        Assert.That(details.LastStatusWord, Is.EqualTo("91BE"));
        Assert.That(details.Error, Does.Contain("boundary error"));
        Assert.That(details.Certificate, Is.EqualTo(LeafVectors.CardCert[..263]));
    }

    [Test]
    public void Detect_FileNotDer_IsUnreadableWithHeaderInError()
    {
        var card = new FakeLeafCard { File2 = [0x00, 0x01, 0x93, 0x30, 0x82, 0x01, 0x39, 0xA0, .. new byte[400]] };

        var details = Detect(card, TestCa);

        Assert.That(details.Trust, Is.EqualTo(LeafTrust.Unreadable));
        Assert.That(details.Error, Does.Contain("00019330820139A0"));
    }

    [Test]
    public void Detect_ImplausibleLength_IsUnreadable()
    {
        var card = new FakeLeafCard { File2 = [0x30, 0x82, 0x10, 0x00, .. new byte[4100]] };

        var details = Detect(card, TestCa);

        Assert.That(details.Error, Does.Contain("Implausible certificate length"));
    }

    [Test]
    public void Detect_NonDigitOpenId_IsProfileViolation()
    {
        var cert = LeafVectors.CardCert.ToArray();
        cert[LeafVectors.OffOpenIdDigits] = (byte)'A';
        var card = new FakeLeafCard { File2 = cert };

        var details = Detect(card, TestCa);

        Assert.That(details.Trust, Is.EqualTo(LeafTrust.ProfileViolation));
        Assert.That(details.OpenId, Is.Null);
        Assert.That(details.ProfileIssues, Has.Some.Contains("1-12 decimal digits"));
        Assert.That(details.TrustAnchorName, Is.Null, "A mutated tbsCertificate no longer verifies");
    }

    [Test]
    public void Detect_OutOfRangeOpenId_IsProfileViolation()
    {
        // 300000000000 > 2^38 - 1
        var cert = LeafVectors.CardCert.ToArray();
        cert[LeafVectors.OffOpenIdDigits] = (byte)'3';
        var card = new FakeLeafCard { File2 = cert };

        var details = Detect(card, TestCa);

        Assert.That(details.Trust, Is.EqualTo(LeafTrust.ProfileViolation));
        Assert.That(details.ProfileIssues, Has.Some.Contains("38 bits"));
    }

    [Test]
    public void Detect_TransportFailureAfterSelect_StillDetected()
    {
        var connection = new Moq.Mock<CredBench.Core.Services.ICardConnection>();
        connection.Setup(c => c.Transmit(Moq.It.Is<byte[]>(b => b[1] == 0x5A))).Returns([0x91, 0x00]);
        connection.Setup(c => c.Transmit(Moq.It.Is<byte[]>(b => b[1] == 0xAD))).Throws(new InvalidOperationException("Card removed"));

        var (detected, _, typed) = CreateDetector(TestCa).Detect(connection.Object);

        Assert.That(detected, Is.True);
        Assert.That(((LEAFVerifiedDetails)typed!).Error, Is.EqualTo("Card removed"));
    }

    [Test]
    public void Detect_NdefApplication_ReadsTapUrl()
    {
        const string url = "d.leaf-community.com?uid=AC3AA2933D7F2E98FB19A9014F166D5D&bid=D92123047DE7E6077F441E200154C4A9&cmac=F29577D2F8A16D95";
        var card = new FakeLeafCard { NdefFile = FakeLeafCard.NdefFileWithUri(url) };

        var details = Detect(card, TestCa);

        Assert.That(details.TapUrl, Is.EqualTo("https://" + url));
        Assert.That(details.IsFullyVerified, Is.True, "Reading the tap URL must not disturb the credential");
        Assert.That(details.GetFields(), Has.Some.EqualTo(("Tap URL", "https://" + url)));
    }

    [Test]
    public void Detect_NoNdefApplication_TapUrlIsNull()
    {
        var details = Detect(new FakeLeafCard(), TestCa);

        Assert.That(details.TapUrl, Is.Null);
        Assert.That(details.IsFullyVerified, Is.True);
    }

    [Test]
    public void Detect_UnreadableCertificate_StillReadsTapUrl()
    {
        var card = new FakeLeafCard
        {
            ReadError = (0, [0x91, 0xAE]),
            NdefFile = FakeLeafCard.NdefFileWithUri("d.leaf-community.com?uid=00")
        };

        var details = Detect(card, TestCa);

        Assert.That(details.Trust, Is.EqualTo(LeafTrust.Unreadable));
        Assert.That(details.TapUrl, Is.EqualTo("https://d.leaf-community.com?uid=00"));
    }

    [Test]
    public void EvaluateCertificate_Garbage_IsUnreadable()
    {
        var details = LEAFVerifiedDetector.EvaluateCertificate([0x30, 0x03, 0x02, 0x01], [TestCa]);

        Assert.That(details.Trust, Is.EqualTo(LeafTrust.Unreadable));
        Assert.That(details.Error, Is.Not.Null);
    }
}
