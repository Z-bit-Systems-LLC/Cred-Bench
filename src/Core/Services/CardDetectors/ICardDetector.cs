using CredBench.Core.Models;

namespace CredBench.Core.Services.CardDetectors;

public interface ICardDetector
{
    CardTechnology Technology { get; }
    (bool Detected, string? Details) Detect(ICardConnection connection);
}
