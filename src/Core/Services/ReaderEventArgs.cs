namespace CredBench.Core.Services;

public class ReaderEventArgs : EventArgs
{
    public required string ReaderName { get; init; }
    public string? ATR { get; init; }
}
