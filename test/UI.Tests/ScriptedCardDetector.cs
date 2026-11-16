using CredBench.Core.Models;
using CredBench.Core.Services;
using CredBench.Core.Services.CardDetectors;

namespace CredBench.UI.Tests;

/// <summary>
/// A detector whose answer a test sets, standing in for the real detector of one technology so
/// the UI can be driven through every detection outcome without a card or a reader.
/// </summary>
/// <remarks>
/// Detection runs on a thread pool thread while the test thread sets up the next answer, so the
/// answer is swapped as a whole rather than built up field by field.
/// </remarks>
public sealed class ScriptedCardDetector(CardTechnology technology) : ICardDetector
{
    private static readonly Func<ICardConnection, (bool, string?, object?)> NotDetected = _ => (false, null, null);

    private volatile Func<ICardConnection, (bool, string?, object?)> _detect = NotDetected;

    /// <inheritdoc />
    public CardTechnology Technology => technology;

    /// <summary>
    /// Reports the technology as present with the given details.
    /// </summary>
    public void Detects(object? typedDetails, string? details = null) =>
        _detect = _ => (true, details, typedDetails);

    /// <summary>
    /// Waits for <paramref name="release"/> before reporting the technology as absent, which holds
    /// the scan open so a test can look at the app while it is scanning.
    /// </summary>
    public void BlocksUntil(ManualResetEventSlim release) =>
        _detect = _ =>
        {
            release.Wait(TimeSpan.FromSeconds(30));
            return (false, null, null);
        };

    /// <inheritdoc />
    public (bool Detected, string? Details, object? TypedDetails) Detect(ICardConnection connection) =>
        _detect(connection);
}
