namespace CredBench.Core.Models.TechnologyDetails;

public record PIVDetails
{
    public string Status { get; init; } = "Detected";
    public string? CHUID { get; init; }
    public string? FASCN { get; init; }
    public IReadOnlyList<string> KeySlots { get; init; } = [];
    public IReadOnlyList<string> Certificates { get; init; } = [];
}
