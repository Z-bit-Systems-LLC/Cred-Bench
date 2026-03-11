using System.Security.Cryptography;

namespace CredBench.Core.Services.GlobalPlatform;

/// <summary>
/// SCP02 secure channel session. Handles session key derivation, cryptogram
/// computation, and C-MAC generation per GP Card Specification 2.1.1 Appendix B.
/// </summary>
public class Scp02Session
{
    private byte[] _sessionEncKey = null!;
    private byte[] _sessionMacKey = null!;
    private byte[] _sessionDekKey = null!;
    private byte[] _icv = new byte[8];

    public byte[] SequenceCounter { get; private set; } = null!;

    /// <summary>
    /// Session DEK key, used externally for key encryption during PUT KEY.
    /// </summary>
    public byte[] SessionDekKey => _sessionDekKey;

    // Derivation constants per GP 2.1.1 Appendix B.1.2
    private static readonly byte[] DerivationConstantEnc = [0x01, 0x82];
    private static readonly byte[] DerivationConstantMac = [0x01, 0x01];
    private static readonly byte[] DerivationConstantDek = [0x01, 0x81];

    /// <summary>
    /// Derives SCP02 session keys from static keys and the card's sequence counter.
    /// </summary>
    public void DeriveSessionKeys(
        byte[] staticEncKey, byte[] staticMacKey, byte[] staticDekKey,
        byte[] sequenceCounter)
    {
        SequenceCounter = sequenceCounter;
        _sessionEncKey = DeriveKey(staticEncKey, DerivationConstantEnc, sequenceCounter);
        _sessionMacKey = DeriveKey(staticMacKey, DerivationConstantMac, sequenceCounter);
        _sessionDekKey = DeriveKey(staticDekKey, DerivationConstantDek, sequenceCounter);
    }

    /// <summary>
    /// Verifies the card cryptogram from INITIALIZE UPDATE.
    /// Cryptogram = full 3DES-CBC MAC of (host_challenge ‖ seq_counter ‖ card_challenge).
    /// </summary>
    public bool VerifyCardCryptogram(
        byte[] hostChallenge, byte[] cardChallenge, byte[] expectedCryptogram)
    {
        // Input: host_challenge(8) || sequence_counter(2) || card_challenge(6) = 16 bytes
        var data = new byte[16];
        Buffer.BlockCopy(hostChallenge, 0, data, 0, 8);
        Buffer.BlockCopy(SequenceCounter, 0, data, 8, 2);
        Buffer.BlockCopy(cardChallenge, 0, data, 10, 6);

        var computed = FullTripleDesCbcMac(data, _sessionEncKey);
        return CryptographicOperations.FixedTimeEquals(computed, expectedCryptogram);
    }

    /// <summary>
    /// Computes host cryptogram for EXTERNAL AUTHENTICATE.
    /// Cryptogram = full 3DES-CBC MAC of (seq_counter ‖ card_challenge ‖ host_challenge).
    /// </summary>
    public byte[] ComputeHostCryptogram(byte[] cardChallenge, byte[] hostChallenge)
    {
        // Input: sequence_counter(2) || card_challenge(6) || host_challenge(8) = 16 bytes
        var data = new byte[16];
        Buffer.BlockCopy(SequenceCounter, 0, data, 0, 2);
        Buffer.BlockCopy(cardChallenge, 0, data, 2, 6);
        Buffer.BlockCopy(hostChallenge, 0, data, 8, 8);

        return FullTripleDesCbcMac(data, _sessionEncKey);
    }

    /// <summary>
    /// Wraps a command APDU with C-MAC. Sets the secure messaging bit in CLA,
    /// adjusts Lc to include the 8-byte MAC, and appends the MAC.
    /// </summary>
    public byte[] WrapCommand(byte[] command)
    {
        byte cla = (byte)(command[0] | 0x04);
        byte ins = command[1];
        byte p1 = command[2];
        byte p2 = command[3];

        int dataLength = command.Length > 4 ? command[4] : 0;
        byte[] data = command.Length > 5 ? command[5..(5 + dataLength)] : [];

        byte newLc = (byte)(dataLength + 8);

        // MAC input: modified header + data
        var macInput = new byte[5 + data.Length];
        macInput[0] = cla;
        macInput[1] = ins;
        macInput[2] = p1;
        macInput[3] = p2;
        macInput[4] = newLc;
        if (data.Length > 0)
            Buffer.BlockCopy(data, 0, macInput, 5, data.Length);

        var mac = RetailMac(macInput, _sessionMacKey, _icv);
        _icv = mac; // SCP02 i=15: next ICV = this C-MAC

        // Final APDU: CLA' INS P1 P2 Lc' Data MAC
        var wrapped = new byte[5 + data.Length + 8];
        wrapped[0] = cla;
        wrapped[1] = ins;
        wrapped[2] = p1;
        wrapped[3] = p2;
        wrapped[4] = newLc;
        if (data.Length > 0)
            Buffer.BlockCopy(data, 0, wrapped, 5, data.Length);
        Buffer.BlockCopy(mac, 0, wrapped, 5 + data.Length, 8);

        return wrapped;
    }

    // ── Crypto primitives ──────────────────────────────────────────────

    /// <summary>
    /// Derives a session key by 3DES-CBC encrypting the derivation data with the static key.
    /// Derivation data = constant(2) ‖ sequence_counter(2) ‖ 0x00(12).
    /// </summary>
    private static byte[] DeriveKey(byte[] staticKey, byte[] constant, byte[] sequenceCounter)
    {
        var derivationData = new byte[16];
        derivationData[0] = constant[0];
        derivationData[1] = constant[1];
        derivationData[2] = sequenceCounter[0];
        derivationData[3] = sequenceCounter[1];
        // bytes 4–15 stay 0x00

        return TripleDesCbcEncrypt(derivationData, staticKey, new byte[8]);
    }

    /// <summary>
    /// Full 3DES-CBC MAC: encrypt data with 3DES-CBC, return last 8 bytes.
    /// Data length must be a multiple of 8.
    /// </summary>
    internal static byte[] FullTripleDesCbcMac(byte[] data, byte[] key)
    {
        var encrypted = TripleDesCbcEncrypt(data, key, new byte[8]);
        return encrypted[^8..];
    }

    /// <summary>
    /// ISO 9797-1 MAC Algorithm 3 (Retail MAC) with padding method 2.
    /// 1. Single-DES CBC with K1 over all padded blocks → intermediate
    /// 2. Decrypt intermediate with single DES K2
    /// 3. Encrypt result with single DES K1
    /// </summary>
    internal static byte[] RetailMac(byte[] data, byte[] key, byte[] icv)
    {
        var k1 = key[..8];
        var k2 = key[8..16];

        var padded = Pad80(data);
        var intermediate = DesCbcMac(padded, k1, icv);
        var decrypted = DesEcbProcess(intermediate, k2, encrypting: false);
        return DesEcbProcess(decrypted, k1, encrypting: true);
    }

    /// <summary>
    /// 3DES-CBC encrypt. Returns full ciphertext (same length as input).
    /// </summary>
    internal static byte[] TripleDesCbcEncrypt(byte[] data, byte[] key, byte[] iv)
    {
        using var tdes = TripleDES.Create();
        tdes.Key = EnsureKey24(key);
        tdes.IV = iv;
        tdes.Mode = CipherMode.CBC;
        tdes.Padding = PaddingMode.None;

        using var enc = tdes.CreateEncryptor();
        return enc.TransformFinalBlock(data, 0, data.Length);
    }

    /// <summary>
    /// Single-DES CBC MAC: DES-CBC encrypt, return last 8 bytes.
    /// </summary>
    private static byte[] DesCbcMac(byte[] data, byte[] key, byte[] iv)
    {
        using var des = DES.Create();
        des.Key = key;
        des.IV = iv;
        des.Mode = CipherMode.CBC;
        des.Padding = PaddingMode.None;

        using var enc = des.CreateEncryptor();
        var ct = enc.TransformFinalBlock(data, 0, data.Length);
        return ct[^8..];
    }

    private static byte[] DesEcbProcess(byte[] block, byte[] key, bool encrypting)
    {
        using var des = DES.Create();
        des.Key = key;
        des.Mode = CipherMode.ECB;
        des.Padding = PaddingMode.None;

        using var transform = encrypting ? des.CreateEncryptor() : des.CreateDecryptor();
        return transform.TransformFinalBlock(block, 0, block.Length);
    }

    /// <summary>
    /// ISO 9797 padding method 2: append 0x80, then 0x00s to 8-byte boundary.
    /// </summary>
    internal static byte[] Pad80(byte[] data)
    {
        int paddedLength = ((data.Length / 8) + 1) * 8;
        var padded = new byte[paddedLength];
        Buffer.BlockCopy(data, 0, padded, 0, data.Length);
        padded[data.Length] = 0x80;
        return padded;
    }

    /// <summary>
    /// Ensures key is 24 bytes for .NET TripleDES (16-byte key → K1‖K2‖K1).
    /// </summary>
    private static byte[] EnsureKey24(byte[] key)
    {
        if (key.Length == 24) return key;
        if (key.Length != 16)
            throw new ArgumentException($"Invalid 3DES key length: {key.Length}");

        var key24 = new byte[24];
        Buffer.BlockCopy(key, 0, key24, 0, 16);
        Buffer.BlockCopy(key, 0, key24, 16, 8);
        return key24;
    }
}
