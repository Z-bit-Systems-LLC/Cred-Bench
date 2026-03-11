namespace CredBench.Core.Services.GlobalPlatform;

/// <summary>
/// GlobalPlatform key diversification functions.
/// Derives per-card static keys from a master key and the card's key diversification data.
/// </summary>
internal static class KeyDiversification
{
    /// <summary>
    /// VISA2 key diversification. Uses bytes 4–9 of the 10-byte key diversification data.
    /// For each key type, constructs a 16-byte derivation block and encrypts with 3DES-ECB.
    /// </summary>
    public static (byte[] Enc, byte[] Mac, byte[] Dek) Visa2(byte[] masterKey, byte[] kdd)
    {
        return (
            DeriveVisa2Key(masterKey, kdd, 0x01),
            DeriveVisa2Key(masterKey, kdd, 0x02),
            DeriveVisa2Key(masterKey, kdd, 0x03));
    }

    /// <summary>
    /// EMV key diversification. Uses bytes 0–5 of the 10-byte key diversification data.
    /// </summary>
    public static (byte[] Enc, byte[] Mac, byte[] Dek) Emv(byte[] masterKey, byte[] kdd)
    {
        return (
            DeriveEmvKey(masterKey, kdd, 0x01),
            DeriveEmvKey(masterKey, kdd, 0x02),
            DeriveEmvKey(masterKey, kdd, 0x03));
    }

    private static byte[] DeriveVisa2Key(byte[] masterKey, byte[] kdd, byte keyType)
    {
        // VISA2: uses kdd[4..9] (6 bytes)
        // Left half:  kdd[4..9] || F0 || keyType
        // Right half: kdd[4..9] || 0F || keyType
        var derivationData = new byte[16];
        Buffer.BlockCopy(kdd, 4, derivationData, 0, 6);
        derivationData[6] = 0xF0;
        derivationData[7] = keyType;
        Buffer.BlockCopy(kdd, 4, derivationData, 8, 6);
        derivationData[14] = 0x0F;
        derivationData[15] = keyType;

        return Scp02Session.TripleDesCbcEncrypt(derivationData, masterKey, new byte[8]);
    }

    private static byte[] DeriveEmvKey(byte[] masterKey, byte[] kdd, byte keyType)
    {
        // EMV: uses kdd[0..5] (6 bytes)
        // Left half:  kdd[0..5] || F0 || keyType
        // Right half: kdd[0..5] || 0F || keyType
        var derivationData = new byte[16];
        Buffer.BlockCopy(kdd, 0, derivationData, 0, 6);
        derivationData[6] = 0xF0;
        derivationData[7] = keyType;
        Buffer.BlockCopy(kdd, 0, derivationData, 8, 6);
        derivationData[14] = 0x0F;
        derivationData[15] = keyType;

        return Scp02Session.TripleDesCbcEncrypt(derivationData, masterKey, new byte[8]);
    }
}
