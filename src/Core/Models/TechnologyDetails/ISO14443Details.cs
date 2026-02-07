namespace CredBench.Core.Models.TechnologyDetails;

public record ISO14443Details
{
    public string? UID { get; init; }
    public string? CSN { get; init; }
    public string? Manufacturer { get; init; }
    public byte? SAK { get; init; }
    public string? CardType { get; init; }
    public string? UIDLength { get; init; }

    public int? BitCount => UID is { Length: > 0 } ? UID.Length / 2 * 8 : null;
    public string WiegandBitsLabel => BitCount is { } count ? $"Wiegand Bits ({count})" : "Wiegand Bits";
    public string? UIDBytes => HexToDashed(UID);
    public string? CSNWiegandBits => HexToBinary(CSN);

    private static string? HexToDashed(string? hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length < 2)
            return hex;

        return string.Join("-", Enumerable.Range(0, hex.Length / 2)
            .Select(i => hex.Substring(i * 2, 2)));
    }

    private static string? HexToBinary(string? hex)
    {
        if (string.IsNullOrEmpty(hex))
            return null;

        var bits = new char[hex.Length * 4 + (hex.Length / 2 - 1)];
        var pos = 0;

        for (var i = 0; i < hex.Length; i += 2)
        {
            if (pos > 0)
                bits[pos++] = ' ';

            var value = Convert.ToByte(hex.Substring(i, 2), 16);
            for (var bit = 7; bit >= 0; bit--)
                bits[pos++] = (value & (1 << bit)) != 0 ? '1' : '0';
        }

        return new string(bits, 0, pos);
    }
}
