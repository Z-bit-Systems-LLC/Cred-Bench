namespace CredBench.Core.Models;

public record ReaderInfo
{
    public required string Name { get; init; }
    public ReaderStatus Status { get; init; }
    public bool HasCard { get; init; }
}

public enum ReaderStatus
{
    Unknown,
    Available,
    InUse,
    Unavailable
}
