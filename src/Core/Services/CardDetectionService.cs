using CredBench.Core.Models;
using CredBench.Core.Models.TechnologyDetails;
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

            // Typed details
            PIVDetails? pivDetails = null;
            DESFireDetails? desfireDetails = null;
            ISO14443Details? iso14443Details = null;
            PKOCDetails? pkocDetails = null;
            LEAFDetails? leafDetails = null;

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

                        var (detected, detectorDetails, typedDetails) = detector.Detect(connection);

                        if (detected)
                        {
                            technologies |= detector.Technology;

                            if (!string.IsNullOrEmpty(detectorDetails))
                            {
                                details[detector.Technology] = detectorDetails;
                            }

                            // Store typed details by technology
                            switch (typedDetails)
                            {
                                case PIVDetails piv:
                                    pivDetails = piv;
                                    break;
                                case DESFireDetails desfire:
                                    desfireDetails = desfire;
                                    break;
                                case ISO14443Details iso:
                                    iso14443Details = iso;
                                    break;
                                case PKOCDetails pkoc:
                                    pkocDetails = pkoc;
                                    break;
                                case LEAFDetails leaf:
                                    leafDetails = leaf;
                                    break;
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

            // Build general card details
            var generalDetails = BuildGeneralDetails(atr, uid, iso14443Details, technologies);

            return new DetectionResult
            {
                Technologies = technologies,
                ATR = atr,
                UID = uid,
                DetectedAIDs = detectedAids,
                Details = details,
                GeneralDetails = generalDetails,
                PIVDetails = pivDetails,
                DESFireDetails = desfireDetails,
                ISO14443Details = iso14443Details,
                PKOCDetails = pkocDetails,
                LEAFDetails = leafDetails
            };
        }, cancellationToken);
    }

    private static GeneralCardDetails BuildGeneralDetails(
        string? atr,
        string? uid,
        ISO14443Details? iso14443Details,
        CardTechnology technologies)
    {
        // CSN comes from ISO14443 detection if available
        var csn = iso14443Details?.CSN;

        // Build card type summary from detected technologies
        var typeNames = new List<string>();
        if (technologies.HasFlag(CardTechnology.PIV))
            typeNames.Add("PIV");
        if (technologies.HasFlag(CardTechnology.DESFire))
            typeNames.Add("DESFire");
        if (technologies.HasFlag(CardTechnology.PKOC))
            typeNames.Add("PKOC");
        if (technologies.HasFlag(CardTechnology.LEAF))
            typeNames.Add("LEAF");

        var summary = typeNames.Count > 0
            ? string.Join(" + ", typeNames)
            : (technologies.HasFlag(CardTechnology.ISO14443) ? "ISO14443 Contactless" : "Unknown Card");

        return new GeneralCardDetails
        {
            ATR = atr,
            UID = uid,
            CSN = csn,
            CardTypeSummary = summary
        };
    }
}
