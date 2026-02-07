namespace CredBench.Core.Models.TechnologyDetails;

public record PIVDetails
{
    public string Status { get; init; } = "Detected";
    public string? CHUID { get; init; }
    public string? FASCN { get; init; }
    public string? FASCNHex { get; init; }

    // Parsed FASC-N fields (from decoded 200-bit BCD string)
    // Format: S[Agency 4]F[System 4]F[Credential 6]F[CS 1]F[ICI 1]F[PI 10][OC 1][OI 4][POA 1]E[LRC 1]
    public string? AgencyCode => ParseField(1, 4);
    public string? SystemCode => ParseField(6, 4);
    public string? CredentialNumber => ParseField(11, 6);
    public string? CredentialSeries => ParseField(18, 1);
    public string? IndividualCredentialIssue => ParseField(20, 1);
    public string? PersonIdentifier => ParseField(22, 10);
    public string? OrganizationalCategory => ParseField(32, 1);
    public string? OrganizationalIdentifier => ParseField(33, 4);
    public string? PersonOrgAssociation => ParseField(37, 1);

    // 200-bit Wiegand display from raw FASC-N bytes
    public string WiegandBitsLabel => FASCNHex is { Length: > 0 }
        ? $"Wiegand Bits ({FASCNHex.Length / 2 * 8})"
        : "Wiegand Bits";
    public string? FASCNWiegandBits => HexToBinary(FASCNHex);

    private string? ParseField(int start, int length)
    {
        if (FASCN is null || FASCN.Length < start + length)
            return null;

        var value = FASCN.Substring(start, length);
        return value.Contains('?') ? null : value;
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
