namespace CredBench.Core.Models.TechnologyDetails;

public record ISO14443Details
{
    public string? UID { get; init; }
    public string? CSN { get; init; }
    public string? Manufacturer { get; init; }
    public byte? SAK { get; init; }
    public string? CardType { get; init; }
    public string? UIDLength { get; init; }
}
