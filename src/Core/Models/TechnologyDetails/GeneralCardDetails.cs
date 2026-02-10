namespace CredBench.Core.Models.TechnologyDetails;

/// <summary>
/// General smart card details common to all card technologies, including reader info, ATR, and UID.
/// </summary>
public record GeneralCardDetails
{
    public string? ReaderName { get; init; }
    public string? Protocol { get; init; }
    public string? ATR { get; init; }
    public string? UID { get; init; }
    public string? CSN { get; init; }
    public string? CardTypeSummary { get; init; }

    /// <summary>
    /// Returns non-null fields as labeled display pairs for text output.
    /// </summary>
    public IReadOnlyList<(string Label, string Value)> GetFields()
    {
        List<(string, string)> fields = [];
        if (ReaderName is { } reader) fields.Add(("Reader", reader));
        if (Protocol is { } protocol) fields.Add(("Protocol", protocol));
        if (ATR is { } atr) fields.Add(("ATR", atr));
        if (UID is { } uid) fields.Add(("UID", uid));
        if (CSN is { } csn) fields.Add(("CSN", csn));
        if (CardTypeSummary is { } cardType) fields.Add(("Card Type", cardType));
        return fields;
    }
}
