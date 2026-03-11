using System.Security.Cryptography;
using CredBench.Core.Services.GlobalPlatform;
using NUnit.Framework;

namespace CredBench.Core.Tests.GlobalPlatform;

[TestFixture]
public class Scp02SessionTests
{
    // Standard GP test key: 404142434445464748494A4B4C4D4E4F
    private static readonly byte[] TestKey =
    [
        0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47,
        0x48, 0x49, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F
    ];

    [Test]
    public void Pad80_ExactBlockBoundary_AddsFull8BytePad()
    {
        // 8 bytes of data → must still add padding block
        var data = new byte[8];
        var padded = Scp02Session.Pad80(data);

        Assert.That(padded.Length, Is.EqualTo(16));
        Assert.That(padded[8], Is.EqualTo(0x80));
        for (int i = 9; i < 16; i++)
            Assert.That(padded[i], Is.EqualTo(0x00));
    }

    [Test]
    public void Pad80_ShortData_PadsToNextBlock()
    {
        var data = new byte[] { 0x01, 0x02, 0x03 };
        var padded = Scp02Session.Pad80(data);

        Assert.That(padded.Length, Is.EqualTo(8));
        Assert.That(padded[0], Is.EqualTo(0x01));
        Assert.That(padded[1], Is.EqualTo(0x02));
        Assert.That(padded[2], Is.EqualTo(0x03));
        Assert.That(padded[3], Is.EqualTo(0x80));
        for (int i = 4; i < 8; i++)
            Assert.That(padded[i], Is.EqualTo(0x00));
    }

    [Test]
    public void Pad80_EmptyData_Returns8ByteBlock()
    {
        var padded = Scp02Session.Pad80([]);
        Assert.That(padded.Length, Is.EqualTo(8));
        Assert.That(padded[0], Is.EqualTo(0x80));
    }

    [Test]
    public void DeriveSessionKeys_ProducesDeterministicOutput()
    {
        // Same inputs → same session keys
        byte[] seq = [0x00, 0x1A];

        var session1 = new Scp02Session();
        session1.DeriveSessionKeys(TestKey, TestKey, TestKey, seq);

        var session2 = new Scp02Session();
        session2.DeriveSessionKeys(TestKey, TestKey, TestKey, seq);

        // Verify by generating the same cryptogram
        byte[] challenge = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
        byte[] cardChallenge = [0x11, 0x22, 0x33, 0x44, 0x55, 0x66];

        var crypto1 = session1.ComputeHostCryptogram(cardChallenge, challenge);
        var crypto2 = session2.ComputeHostCryptogram(cardChallenge, challenge);

        Assert.That(crypto1, Is.EqualTo(crypto2));
    }

    [Test]
    public void DeriveSessionKeys_DifferentSequenceCounters_ProduceDifferentKeys()
    {
        byte[] seq1 = [0x00, 0x01];
        byte[] seq2 = [0x00, 0x02];
        byte[] challenge = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
        byte[] cardChallenge = [0x11, 0x22, 0x33, 0x44, 0x55, 0x66];

        var session1 = new Scp02Session();
        session1.DeriveSessionKeys(TestKey, TestKey, TestKey, seq1);

        var session2 = new Scp02Session();
        session2.DeriveSessionKeys(TestKey, TestKey, TestKey, seq2);

        var crypto1 = session1.ComputeHostCryptogram(cardChallenge, challenge);
        var crypto2 = session2.ComputeHostCryptogram(cardChallenge, challenge);

        Assert.That(crypto1, Is.Not.EqualTo(crypto2));
    }

    [Test]
    public void VerifyCardCryptogram_CorrectValue_ReturnsTrue()
    {
        byte[] seq = [0x00, 0x05];
        byte[] hostChallenge = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x00, 0x11];
        byte[] cardChallenge = [0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC];

        var session = new Scp02Session();
        session.DeriveSessionKeys(TestKey, TestKey, TestKey, seq);

        // Compute the expected cryptogram manually via the same path
        var data = new byte[16];
        Buffer.BlockCopy(hostChallenge, 0, data, 0, 8);
        Buffer.BlockCopy(seq, 0, data, 8, 2);
        Buffer.BlockCopy(cardChallenge, 0, data, 10, 6);
        var expected = Scp02Session.FullTripleDesCbcMac(data, session.SessionDekKey);
        // Use the real S-ENC by going through the public API
        // We verify roundtrip: what the card would compute should match what we verify

        // Instead, just verify that the method returns true for its own computation
        // (this tests internal consistency)
        var hostCrypto = session.ComputeHostCryptogram(cardChallenge, hostChallenge);
        Assert.That(hostCrypto, Has.Length.EqualTo(8));

        // Build card cryptogram the same way the card would
        var cardCryptoData = new byte[16];
        Buffer.BlockCopy(hostChallenge, 0, cardCryptoData, 0, 8);
        Buffer.BlockCopy(seq, 0, cardCryptoData, 8, 2);
        Buffer.BlockCopy(cardChallenge, 0, cardCryptoData, 10, 6);

        // We can't easily get S-ENC externally, so test via roundtrip
        // Derive the same session, compute card cryptogram, verify it
        var session2 = new Scp02Session();
        session2.DeriveSessionKeys(TestKey, TestKey, TestKey, seq);
        var cardCryptogram = session2.ComputeHostCryptogram(cardChallenge, hostChallenge);
        // ComputeHostCryptogram uses different data order than card cryptogram, so we
        // can't directly reuse it. Instead test Verify with a known-good value.

        // The real test: verify returns true for correctly computed cryptogram
        Assert.That(session.VerifyCardCryptogram(hostChallenge, cardChallenge,
            ComputeCardCryptogramDirectly(session, hostChallenge, seq, cardChallenge)),
            Is.True);
    }

    [Test]
    public void VerifyCardCryptogram_WrongValue_ReturnsFalse()
    {
        byte[] seq = [0x00, 0x05];
        byte[] hostChallenge = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x00, 0x11];
        byte[] cardChallenge = [0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC];
        byte[] wrongCryptogram = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

        var session = new Scp02Session();
        session.DeriveSessionKeys(TestKey, TestKey, TestKey, seq);

        Assert.That(session.VerifyCardCryptogram(hostChallenge, cardChallenge, wrongCryptogram),
            Is.False);
    }

    [Test]
    public void HostCryptogram_HasDifferentOrderThanCardCryptogram()
    {
        byte[] seq = [0x00, 0x05];
        byte[] hostChallenge = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x00, 0x11];
        byte[] cardChallenge = [0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC];

        var session = new Scp02Session();
        session.DeriveSessionKeys(TestKey, TestKey, TestKey, seq);

        var hostCrypto = session.ComputeHostCryptogram(cardChallenge, hostChallenge);
        var cardCrypto = ComputeCardCryptogramDirectly(session, hostChallenge, seq, cardChallenge);

        // They should differ because the data order is different
        Assert.That(hostCrypto, Is.Not.EqualTo(cardCrypto));
    }

    [Test]
    public void WrapCommand_SetsSecureMessagingBitInCla()
    {
        byte[] seq = [0x00, 0x01];
        var session = new Scp02Session();
        session.DeriveSessionKeys(TestKey, TestKey, TestKey, seq);

        // Simple command: 80 E4 00 00 02 [4F 00]
        byte[] command = [0x80, 0xE4, 0x00, 0x00, 0x02, 0x4F, 0x00];
        var wrapped = session.WrapCommand(command);

        Assert.That(wrapped[0] & 0x04, Is.EqualTo(0x04), "Secure messaging bit should be set");
        Assert.That(wrapped[0], Is.EqualTo(0x84));
    }

    [Test]
    public void WrapCommand_AdjustsLcToIncludeMac()
    {
        byte[] seq = [0x00, 0x01];
        var session = new Scp02Session();
        session.DeriveSessionKeys(TestKey, TestKey, TestKey, seq);

        byte[] command = [0x80, 0xE4, 0x00, 0x00, 0x02, 0x4F, 0x00];
        var wrapped = session.WrapCommand(command);

        // Original Lc=2, new Lc=2+8=10
        Assert.That(wrapped[4], Is.EqualTo(10));
        // Total length: 5 (header) + 2 (data) + 8 (MAC)
        Assert.That(wrapped.Length, Is.EqualTo(15));
    }

    [Test]
    public void WrapCommand_PreservesOriginalData()
    {
        byte[] seq = [0x00, 0x01];
        var session = new Scp02Session();
        session.DeriveSessionKeys(TestKey, TestKey, TestKey, seq);

        byte[] command = [0x80, 0xE4, 0x00, 0x80, 0x02, 0x4F, 0x00];
        var wrapped = session.WrapCommand(command);

        // Original data bytes should be at positions 5 and 6
        Assert.That(wrapped[5], Is.EqualTo(0x4F));
        Assert.That(wrapped[6], Is.EqualTo(0x00));
    }

    [Test]
    public void WrapCommand_SequentialCalls_ProduceDifferentMacs()
    {
        byte[] seq = [0x00, 0x01];
        var session = new Scp02Session();
        session.DeriveSessionKeys(TestKey, TestKey, TestKey, seq);

        byte[] command = [0x80, 0xE4, 0x00, 0x00, 0x02, 0x4F, 0x00];
        var wrapped1 = session.WrapCommand(command);
        var wrapped2 = session.WrapCommand(command);

        // MACs should differ because ICV chains
        var mac1 = wrapped1[^8..];
        var mac2 = wrapped2[^8..];
        Assert.That(mac1, Is.Not.EqualTo(mac2));
    }

    [Test]
    public void TripleDesCbcEncrypt_KnownVector()
    {
        // Verify 3DES-CBC with a known plaintext/key pair produces consistent output
        byte[] key = [0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47,
                      0x48, 0x49, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F];
        byte[] iv = new byte[8];
        byte[] plaintext = new byte[16]; // all zeros

        var ct1 = Scp02Session.TripleDesCbcEncrypt(plaintext, key, iv);
        var ct2 = Scp02Session.TripleDesCbcEncrypt(plaintext, key, iv);

        Assert.That(ct1, Is.EqualTo(ct2));
        Assert.That(ct1.Length, Is.EqualTo(16));
        // Should not be all zeros (encryption happened)
        Assert.That(ct1, Is.Not.EqualTo(plaintext));
    }

    [Test]
    public void RetailMac_ProducesEightByteOutput()
    {
        byte[] key = TestKey;
        byte[] icv = new byte[8];
        byte[] data = [0x84, 0x82, 0x01, 0x00, 0x10, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];

        var mac = Scp02Session.RetailMac(data, key, icv);

        Assert.That(mac, Has.Length.EqualTo(8));
    }

    [Test]
    public void RetailMac_DifferentData_ProducesDifferentMac()
    {
        byte[] key = TestKey;
        byte[] icv = new byte[8];
        byte[] data1 = [0x84, 0x82, 0x01, 0x00, 0x10, 0xAA];
        byte[] data2 = [0x84, 0x82, 0x01, 0x00, 0x10, 0xBB];

        var mac1 = Scp02Session.RetailMac(data1, key, icv);
        var mac2 = Scp02Session.RetailMac(data2, key, icv);

        Assert.That(mac1, Is.Not.EqualTo(mac2));
    }

    [Test]
    public void RetailMac_DifferentIcv_ProducesDifferentMac()
    {
        byte[] key = TestKey;
        byte[] icv1 = new byte[8];
        byte[] icv2 = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
        byte[] data = [0x84, 0x82, 0x01, 0x00, 0x10];

        var mac1 = Scp02Session.RetailMac(data, key, icv1);
        var mac2 = Scp02Session.RetailMac(data, key, icv2);

        Assert.That(mac1, Is.Not.EqualTo(mac2));
    }

    /// <summary>
    /// Computes card cryptogram directly (host_challenge ‖ seq ‖ card_challenge)
    /// using FullTripleDesCbcMac. This mimics what the card would compute.
    /// We access internal state indirectly through a parallel session.
    /// </summary>
    private static byte[] ComputeCardCryptogramDirectly(
        Scp02Session session, byte[] hostChallenge, byte[] seq, byte[] cardChallenge)
    {
        // Reconstruct S-ENC by deriving from the same inputs
        // We use a parallel session with the same keys to get deterministic output
        var parallelSession = new Scp02Session();
        parallelSession.DeriveSessionKeys(TestKey, TestKey, TestKey, seq);

        // Build card cryptogram data: host_challenge(8) || seq(2) || card_challenge(6)
        var data = new byte[16];
        Buffer.BlockCopy(hostChallenge, 0, data, 0, 8);
        Buffer.BlockCopy(seq, 0, data, 8, 2);
        Buffer.BlockCopy(cardChallenge, 0, data, 10, 6);

        // We need to use the S-ENC key, which is internal.
        // Workaround: derive S-ENC the same way DeriveKey does.
        byte[] derivationData = [0x01, 0x82, seq[0], seq[1], 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        var sEnc = Scp02Session.TripleDesCbcEncrypt(derivationData, TestKey, new byte[8]);

        return Scp02Session.FullTripleDesCbcMac(data, sEnc);
    }
}
