namespace CredBench.Core.Models.TechnologyDetails;

public record DESFireDetails
{
    public string CardType { get; init; } = "DESFire";
    public string? HardwareVersion { get; init; }
    public string? SoftwareVersion { get; init; }
    public string? StorageSize { get; init; }
    public IReadOnlyList<string> Applications { get; init; } = [];
}
