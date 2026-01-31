using CredBench.Core.Models;

namespace CredBench.Core.Services.CardDetectors;

public interface ICardDetector
{
    CardTechnology Technology { get; }
    (bool Detected, string? Details, object? TypedDetails) Detect(ICardConnection connection);
}
