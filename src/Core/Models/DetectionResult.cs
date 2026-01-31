namespace CredBench.Core.Models;

public record DetectionResult
{
    public CardTechnology Technologies { get; init; }
    public string? ATR { get; init; }
    public string? UID { get; init; }
    public IReadOnlyList<string> DetectedAIDs { get; init; } = [];
    public IReadOnlyDictionary<CardTechnology, string> Details { get; init; }
        = new Dictionary<CardTechnology, string>();

    public bool HasTechnology(CardTechnology technology) =>
        (Technologies & technology) == technology;
}
