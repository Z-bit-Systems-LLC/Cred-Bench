namespace CredBench.Core.Services;

public class ReaderEventArgs : EventArgs
{
    public required string ReaderName { get; init; }
    // Carried for subscribers; none reads it yet
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public string? ATR { get; init; }
}
