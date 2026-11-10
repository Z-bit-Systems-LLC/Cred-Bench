namespace CredBench.Core.Models.TechnologyDetails;

/// <summary>
/// MIFARE DESFire card details from GetVersion: product, versions, storage size and production data.
/// </summary>
public record DESFireDetails
{
    /// <summary>Product name, e.g. "DESFire EV3", "DESFire Light", "NTAG 424 DNA".</summary>
    public string CardType { get; init; } = "DESFire";

    /// <summary>Chip vendor from the hardware frame ("NXP", or the raw byte).</summary>
    public string? Vendor { get; init; }

    /// <summary>Hardware major.minor in hex, e.g. "33.0" for EV3.</summary>
    public string? HardwareVersion { get; init; }

    /// <summary>Software major.minor in hex.</summary>
    public string? SoftwareVersion { get; init; }

    /// <summary>Storage capacity, e.g. "8 KB", or a range such as "512 B to 1 KB".</summary>
    public string? StorageSize { get; init; }

    /// <summary>Production batch number from the third GetVersion frame, hex.</summary>
    public string? BatchNumber { get; init; }

    /// <summary>Production week and year, e.g. "Week 23, 2021".</summary>
    public string? ProductionDate { get; init; }

    /// <summary>The raw GetVersion payload (all frames, status words removed), hex.</summary>
    public string? VersionData { get; init; }

    /// <summary>Why the version could not be read, when the card is DESFire but GetVersion failed.</summary>
    public string? VersionUnavailableReason { get; init; }

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
        if (Vendor is { } vendor) fields.Add(("Vendor", vendor));
        if (HardwareVersion is { } hw) fields.Add(("Hardware Version", hw));
        if (SoftwareVersion is { } sw) fields.Add(("Software Version", sw));
        if (VersionUnavailableReason is { } reason) fields.Add(("Version", $"Unavailable ({reason})"));
        if (StorageSize is { } storage) fields.Add(("Storage Size", storage));
        if (ProductionDate is { } date) fields.Add(("Production Date", date));
        if (BatchNumber is { } batch) fields.Add(("Batch Number", batch));
        if (VersionData is { } data) fields.Add(("GetVersion Data", data));
        if (Applications.Count > 0) fields.Add(("Applications", string.Join(", ", Applications)));
        return fields;
    }
}
