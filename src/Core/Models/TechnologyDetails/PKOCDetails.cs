namespace CredBench.Core.Models.TechnologyDetails;

/// <summary>
/// PKOC (Physical Key Open Credential) details including public key, derived credentials, and Wiegand bits.
/// </summary>
public record PKOCDetails
{
    public string? ProtocolVersion { get; init; }
    public string? PublicKeyHex { get; init; }

    // ECC P-256 uncompressed key: 04 [32-byte X] [32-byte Y] = 65 bytes = 130 hex chars
    // Credential is derived from the X component
    public string? PublicKeyX => PublicKeyHex is { Length: >= 66 } ? PublicKeyHex[2..66] : null;

    // Credential sizes per PSIA PKOC spec:
    // 256-bit: Full X component
    // 75-bit:  Lower 75 bits of X (recommended for legacy panels)
    // 64-bit:  Lower 64 bits of X (minimum for legacy panels)
    public string? Credential256Hex => PublicKeyX;
    public string? Credential75Hex => ExtractLowerBitsHex(PublicKeyX, 75);
    public string? Credential64Hex => PublicKeyX is { Length: >= 64 } ? PublicKeyX[48..] : null;

    public string? Credential256Bits => HexToBinary(Credential256Hex);
    public string? Credential75Bits => Credential256Bits is { Length: >= 256 } ? Credential256Bits[181..] : null;
    public string? Credential64Bits => Credential256Bits is { Length: >= 256 } ? Credential256Bits[192..] : null;

    /// <summary>
    /// Returns non-null fields as labeled display pairs for text output.
    /// </summary>
    public IReadOnlyList<(string Label, string Value)> GetFields()
    {
        List<(string, string)> fields = [];
        if (ProtocolVersion is { } version) fields.Add(("Protocol Version", version));
        if (PublicKeyHex is { } key) fields.Add(("Public Key", key));
        if (Credential256Hex is { } c256) fields.Add(("Credential 256-bit", c256));
        if (Credential75Hex is { } c75) fields.Add(("Credential 75-bit", c75));
        if (Credential64Hex is { } c64) fields.Add(("Credential 64-bit", c64));
        if (Credential256Bits is { } bits256) fields.Add(("256-bit Wiegand", bits256));
        if (Credential75Bits is { } bits75) fields.Add(("75-bit Wiegand", bits75));
        if (Credential64Bits is { } bits64) fields.Add(("64-bit Wiegand", bits64));
        return fields;
    }

    private static string? ExtractLowerBitsHex(string? hex, int bitCount)
    {
        if (string.IsNullOrEmpty(hex))
            return null;

        // Convert to bytes, extract lower N bits, return as hex
        var totalBits = hex.Length * 4;
        if (totalBits < bitCount)
            return null;

        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

        var fullBytes = bitCount / 8;
        var remainderBits = bitCount % 8;
        var resultBytes = remainderBits > 0 ? fullBytes + 1 : fullBytes;

        var result = new byte[resultBytes];
        var srcOffset = bytes.Length - fullBytes;

        Array.Copy(bytes, srcOffset, result, remainderBits > 0 ? 1 : 0, fullBytes);

        if (remainderBits > 0)
        {
            var mask = (byte)((1 << remainderBits) - 1);
            result[0] = (byte)(bytes[srcOffset - 1] & mask);
        }

        return BitConverter.ToString(result).Replace("-", "");
    }

    private static string? HexToBinary(string? hex)
    {
        if (string.IsNullOrEmpty(hex))
            return null;

        var bits = new char[hex.Length * 4];
        var pos = 0;

        for (var i = 0; i < hex.Length; i += 2)
        {
            var value = Convert.ToByte(hex.Substring(i, 2), 16);
            for (var bit = 7; bit >= 0; bit--)
                bits[pos++] = (value & (1 << bit)) != 0 ? '1' : '0';
        }

        return new string(bits, 0, pos);
    }
}
