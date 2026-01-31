using System.Diagnostics;
using CredBench.Core.Models;

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

    public (bool Detected, string? Details) Detect(ICardConnection connection)
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

                    // Calculate CSN (reversed byte order, no spaces)
                    var reversedUid = uid.Reverse().ToArray();
                    var csn = BitConverter.ToString(reversedUid).Replace("-", "");

                    // Determine card type based on UID length and first byte
                    var cardType = GetCardType(uid);

                    Debug.WriteLine($"[ISO14443] Detected! UID: {BitConverter.ToString(uid)}, CSN: {csn}, Type: {cardType}");

                    return (true, $"{cardType}, CSN: {csn}");
                }
            }

            Debug.WriteLine("[ISO14443] No valid UID response");
            return (false, null);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ISO14443] Exception: {ex.Message}");
            return (false, null);
        }
    }

    private static string GetCardType(byte[] uid)
    {
        if (uid.Length == 0)
            return "ISO14443";

        // Determine type based on UID length and manufacturer byte
        var uidLength = uid.Length switch
        {
            4 => "4-byte UID",
            7 => "7-byte UID",
            10 => "10-byte UID",
            _ => $"{uid.Length}-byte UID"
        };

        // First byte often indicates manufacturer (for 7-byte UIDs starting with 04 = NXP)
        var manufacturer = uid[0] switch
        {
            0x04 => "NXP",
            0x02 => "ST",
            0x05 => "Infineon",
            0x16 => "Texas Instruments",
            _ => null
        };

        if (manufacturer != null)
            return $"ISO14443 ({manufacturer}, {uidLength})";

        return $"ISO14443 ({uidLength})";
    }
}
