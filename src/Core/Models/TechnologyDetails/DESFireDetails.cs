namespace CredBench.Core.Models.TechnologyDetails;

/// <summary>
/// MIFARE DESFire card details including version info, storage size, and application IDs.
/// </summary>
public record DESFireDetails
{
    public string CardType { get; init; } = "DESFire";
    public string? HardwareVersion { get; init; }
    public string? SoftwareVersion { get; init; }
    public string? StorageSize { get; init; }
    public IReadOnlyList<string> Applications { get; init; } = [];

    /// <summary>
    /// Returns non-null fields as labeled display pairs for text output.
    /// </summary>
    public IReadOnlyList<(string Label, string Value)> GetFields()
    {
        List<(string, string)> fields =
        [
            ("Card Type", CardType)
        ];
        if (HardwareVersion is { } hw) fields.Add(("Hardware Version", hw));
        if (SoftwareVersion is { } sw) fields.Add(("Software Version", sw));
        if (StorageSize is { } storage) fields.Add(("Storage Size", storage));
        if (Applications.Count > 0) fields.Add(("Applications", string.Join(", ", Applications)));
        return fields;
    }
}
