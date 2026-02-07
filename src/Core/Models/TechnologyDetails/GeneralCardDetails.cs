namespace CredBench.Core.Models.TechnologyDetails;

public record GeneralCardDetails
{
    public string? ReaderName { get; init; }
    public string? Protocol { get; init; }
    public string? ATR { get; init; }
    public string? UID { get; init; }
    public string? CSN { get; init; }
    public string? CardTypeSummary { get; init; }
}
