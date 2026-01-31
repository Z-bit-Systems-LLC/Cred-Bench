using CredBench.Core.Models;

namespace CredBench.Core.Services.CardDetectors;

public class IClassDetector : ICardDetector
{
    public CardTechnology Technology => CardTechnology.IClass;

    // Known iClass ATR patterns (partial)
    private static readonly byte[][] KnownIClassAtrPatterns =
    [
        [0x3B, 0x8F, 0x80, 0x01, 0x80, 0x4F], // Common iClass pattern
    ];

    // HID proprietary command to identify iClass
    private static readonly byte[] IdentifyCommand = [0xFF, 0xCA, 0x00, 0x00, 0x00];

    public (bool Detected, string? Details) Detect(ICardConnection connection)
    {
        try
        {
            // First check ATR for known iClass patterns
            var atr = connection.GetATR();
            if (!string.IsNullOrEmpty(atr) && IsIClassAtr(atr))
            {
                return (true, "iClass card detected via ATR");
            }

            // Try Get UID command (works on many readers)
            var response = connection.Transmit(IdentifyCommand);

            if (response.Length >= 2)
            {
                var sw1 = response[^2];
                var sw2 = response[^1];

                // Check if response indicates iClass (reader-specific)
                // Some HID readers return specific data for iClass
                if (sw1 == 0x90 && sw2 == 0x00 && response.Length >= 10)
                {
                    // iClass cards typically have 8-byte CSN
                    var csn = response[..^2];
                    if (csn.Length == 8)
                    {
                        var csnHex = BitConverter.ToString(csn).Replace("-", " ");
                        return (true, $"iClass card, CSN: {csnHex}");
                    }
                }
            }

            return (false, null);
        }
        catch (Exception ex)
        {
            return (false, $"iClass detection error: {ex.Message}");
        }
    }

    private static bool IsIClassAtr(string atrHex)
    {
        var atrBytes = ParseHexString(atrHex);
        if (atrBytes == null)
            return false;

        foreach (var pattern in KnownIClassAtrPatterns)
        {
            if (atrBytes.Length >= pattern.Length)
            {
                var match = true;
                for (var i = 0; i < pattern.Length; i++)
                {
                    if (atrBytes[i] != pattern[i])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                    return true;
            }
        }

        return false;
    }

    private static byte[]? ParseHexString(string hex)
    {
        var cleaned = hex.Replace(" ", "").Replace("-", "");
        if (cleaned.Length % 2 != 0)
            return null;

        try
        {
            var bytes = new byte[cleaned.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(cleaned.Substring(i * 2, 2), 16);
            }

            return bytes;
        }
        catch
        {
            return null;
        }
    }
}
