namespace CredBench.Core.Models;

public record CardInfo
{
    public string? ATR { get; init; }
    public string? UID { get; init; }
    public CardTechnology Technologies { get; init; }
}
