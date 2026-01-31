using CredBench.Core.Models;
using CredBench.Core.Services.CardDetectors;

namespace CredBench.Core.Services;

public class CardDetectionService
{
    private readonly ISmartCardService _smartCardService;
    private readonly IEnumerable<ICardDetector> _detectors;

    public CardDetectionService(
        ISmartCardService smartCardService,
        IEnumerable<ICardDetector> detectors)
    {
        _smartCardService = smartCardService;
        _detectors = detectors;
    }

    public async Task<DetectionResult> DetectAsync(
        string readerName,
        CancellationToken cancellationToken = default)
        => await DetectAsync(readerName, null, cancellationToken);

    public async Task<DetectionResult> DetectAsync(
        string readerName,
        IProgress<CardTechnology>? progress,
        CancellationToken cancellationToken = default)
    {
        // Run all PC/SC operations on a background thread to avoid blocking UI
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var technologies = CardTechnology.Unknown;
            var detectedAids = new List<string>();
            var details = new Dictionary<CardTechnology, string>();
            string? atr = null;
            string? uid = null;

            // Use a single connection for all detection operations
            using (var connection = _smartCardService.Connect(readerName))
            {
                atr = connection.GetATR();
                uid = connection.GetUID();

                foreach (var detector in _detectors)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // Report which technology is currently being scanned
                        progress?.Report(detector.Technology);

                        var (detected, detectorDetails) = detector.Detect(connection);

                        if (detected)
                        {
                            technologies |= detector.Technology;

                            if (!string.IsNullOrEmpty(detectorDetails))
                            {
                                details[detector.Technology] = detectorDetails;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // Individual detector failures should not stop other detections
                    }
                }

                // Report completion (Unknown means done scanning)
                progress?.Report(CardTechnology.Unknown);
            }

            return new DetectionResult
            {
                Technologies = technologies,
                ATR = atr,
                UID = uid,
                DetectedAIDs = detectedAids,
                Details = details
            };
        }, cancellationToken);
    }
}
