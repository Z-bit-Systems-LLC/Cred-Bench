using CredBench.Core.Models.TechnologyDetails;

namespace CredBench.Core.Models;

public record DetectionResult
{
    public CardTechnology Technologies { get; init; }
    public string? ATR { get; init; }
    public string? UID { get; init; }
    public IReadOnlyList<string> DetectedAIDs { get; init; } = [];
    public IReadOnlyDictionary<CardTechnology, string> Details { get; init; }
        = new Dictionary<CardTechnology, string>();

    // Typed detail properties for tabbed display
    public GeneralCardDetails? GeneralDetails { get; init; }
    public PIVDetails? PIVDetails { get; init; }
    public DESFireDetails? DESFireDetails { get; init; }
    public ISO14443Details? ISO14443Details { get; init; }
    public PKOCDetails? PKOCDetails { get; init; }
    public LEAFDetails? LEAFDetails { get; init; }

    public bool HasTechnology(CardTechnology technology) =>
        (Technologies & technology) == technology;
}
