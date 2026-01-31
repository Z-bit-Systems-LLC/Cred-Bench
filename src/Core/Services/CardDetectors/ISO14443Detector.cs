using System.Diagnostics;
using CredBench.Core.Models;
using CredBench.Core.Models.TechnologyDetails;

namespace CredBench.Core.Services.CardDetectors;

/// <summary>
/// Detects any ISO 14443 contactless card and extracts the CSN (Card Serial Number).
/// This is a generic detector that works with any ISO 14443-A or 14443-B compliant card.
/// </summary>
public class ISO14443Detector : ICardDetector
{
    public CardTechnology Technology => CardTechnology.ISO14443;

    // GET UID command (PC/SC pseudo-APDU)
    private static readonly byte[] GetUidCommand = [0xFF, 0xCA, 0x00, 0x00, 0x00];

    public (bool Detected, string? Details, object? TypedDetails) Detect(ICardConnection connection)
    {
        Debug.WriteLine("=== ISO14443 Detection Started ===");

        try
        {
            // Try to get the UID using PC/SC pseudo-APDU
            Debug.WriteLine($"[ISO14443] Sending GET UID: {BitConverter.ToString(GetUidCommand)}");
            var response = connection.Transmit(GetUidCommand);
            Debug.WriteLine($"[ISO14443] Response ({response.Length} bytes): {BitConverter.ToString(response)}");

            if (response.Length >= 2)
            {
                var sw1 = response[^2];
                var sw2 = response[^1];

                if (sw1 == 0x90 && sw2 == 0x00 && response.Length > 2)
                {
                    var uid = response[..^2];
                    var uidHex = BitConverter.ToString(uid).Replace("-", "");

                    // Calculate CSN (reversed byte order, no spaces)
                    var reversedUid = uid.Reverse().ToArray();
                    var csn = BitConverter.ToString(reversedUid).Replace("-", "");

                    // Determine card type based on UID length and first byte
                    var (cardType, manufacturer, uidLengthDesc) = GetCardTypeInfo(uid);

                    Debug.WriteLine($"[ISO14443] Detected! UID: {BitConverter.ToString(uid)}, CSN: {csn}, Type: {cardType}");

                    var details = new ISO14443Details
                    {
                        UID = uidHex,
                        CSN = csn,
                        Manufacturer = manufacturer,
                        CardType = cardType,
                        UIDLength = uidLengthDesc
                    };

                    return (true, $"{cardType}, CSN: {csn}", details);
                }
            }

            Debug.WriteLine("[ISO14443] No valid UID response");
            return (false, null, null);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ISO14443] Exception: {ex.Message}");
            return (false, null, null);
        }
    }

    private static (string CardType, string? Manufacturer, string UIDLength) GetCardTypeInfo(byte[] uid)
    {
        if (uid.Length == 0)
            return ("ISO14443", null, "Unknown");

        // Determine type based on UID length and manufacturer byte
        var uidLength = uid.Length switch
        {
            4 => "4-byte (Single Size)",
            7 => "7-byte (Double Size)",
            10 => "10-byte (Triple Size)",
            _ => $"{uid.Length}-byte"
        };

        // First byte often indicates manufacturer (for 7-byte UIDs starting with 04 = NXP)
        var manufacturer = uid[0] switch
        {
            0x04 => "NXP",
            0x02 => "ST Microelectronics",
            0x05 => "Infineon",
            0x16 => "Texas Instruments",
            _ => null
        };

        var cardType = manufacturer != null
            ? $"ISO14443 ({manufacturer}, {uidLength})"
            : $"ISO14443 ({uidLength})";

        return (cardType, manufacturer, uidLength);
    }
}
