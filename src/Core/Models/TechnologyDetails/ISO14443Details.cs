namespace CredBench.Core.Models.TechnologyDetails;

/// <summary>
/// ISO 14443 contactless card details including UID, SAK, manufacturer, and Wiegand bit representation.
/// </summary>
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

    /// <summary>
    /// Returns non-null fields as labeled display pairs for text output.
    /// </summary>
    public IReadOnlyList<(string Label, string Value)> GetFields()
    {
        List<(string, string)> fields = [];
        if (UID is { } uid) fields.Add(("UID", uid));
        if (UIDBytes is { } uidBytes) fields.Add(("UID Bytes", uidBytes));
        if (UIDLength is { } uidLength) fields.Add(("UID Length", uidLength));
        if (CSN is { } csn) fields.Add(("CSN", csn));
        if (Manufacturer is { } mfr) fields.Add(("Manufacturer", mfr));
        if (SAK is { } sak) fields.Add(("SAK", sak.ToString("X2")));
        if (CardType is { } cardType) fields.Add(("Card Type", cardType));
        if (CSNWiegandBits is { } wiegand) fields.Add((WiegandBitsLabel, wiegand));
        return fields;
    }

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
