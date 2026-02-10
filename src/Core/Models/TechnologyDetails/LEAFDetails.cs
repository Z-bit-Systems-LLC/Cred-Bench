namespace CredBench.Core.Models.TechnologyDetails;

/// <summary>
/// LEAF (Logical Encoding and Application Framework) details including application type and detected AIDs.
/// </summary>
public record LEAFDetails
{
    public string ApplicationType { get; init; } = "Unknown";
    public IReadOnlyList<string> DetectedAIDs { get; init; } = [];

    /// <summary>
    /// Returns non-null fields as labeled display pairs for text output.
    /// </summary>
    public IReadOnlyList<(string Label, string Value)> GetFields()
    {
        List<(string, string)> fields =
        [
            ("Application Type", ApplicationType)
        ];
        if (DetectedAIDs.Count > 0) fields.Add(("AIDs", string.Join(", ", DetectedAIDs)));
        return fields;
    }
}
