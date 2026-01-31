namespace CredBench.Core.Models.TechnologyDetails;

public record LEAFDetails
{
    public string ApplicationType { get; init; } = "Unknown";
    public IReadOnlyList<string> DetectedAIDs { get; init; } = [];
}
