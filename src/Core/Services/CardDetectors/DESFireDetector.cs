using CredBench.Core.Models;

namespace CredBench.Core.Services.CardDetectors;

public class DESFireDetector : ICardDetector
{
    public CardTechnology Technology => CardTechnology.DESFire;

    // DESFire GetVersion command wrapped for PC/SC
    // Native DESFire command: 0x60 (GetVersion)
    // Wrapped as: CLA=90, INS=60, P1=00, P2=00, Le=00
    private static readonly byte[] GetVersionCommand = [0x90, 0x60, 0x00, 0x00, 0x00];

    // Additional Frames command to get more version data
    private static readonly byte[] AdditionalFrameCommand = [0x90, 0xAF, 0x00, 0x00, 0x00];

    public (bool Detected, string? Details) Detect(ICardConnection connection)
    {
        try
        {
            var response = connection.Transmit(GetVersionCommand);

            if (response.Length >= 2)
            {
                var sw1 = response[^2];
                var sw2 = response[^1];

                // DESFire success with more frames: 91 AF
                if (sw1 == 0x91 && sw2 == 0xAF && response.Length >= 9)
                {
                    var versionInfo = ParseVersionInfo(response);

                    // Get second frame
                    var response2 = connection.Transmit(AdditionalFrameCommand);
                    if (response2.Length >= 9 && response2[^2] == 0x91 && response2[^1] == 0xAF)
                    {
                        // Get third frame
                        var response3 = connection.Transmit(AdditionalFrameCommand);
                        if (response3.Length >= 16 && response3[^2] == 0x91 && response3[^1] == 0x00)
                        {
                            // Full version received
                            return (true, versionInfo);
                        }
                    }

                    return (true, versionInfo);
                }

                // DESFire success: 91 00
                if (sw1 == 0x91 && sw2 == 0x00)
                {
                    return (true, "DESFire card detected");
                }
            }

            return (false, null);
        }
        catch (Exception ex)
        {
            return (false, $"DESFire detection error: {ex.Message}");
        }
    }

    private static string ParseVersionInfo(byte[] response)
    {
        if (response.Length < 9)
            return "DESFire card detected";

        var hwMajorVersion = response[3];
        var hwStorageSize = response[5];

        var cardType = hwMajorVersion switch
        {
            0x00 => "DESFire",
            0x01 => "DESFire EV1",
            0x12 => "DESFire EV2",
            0x30 => "DESFire EV3",
            _ => $"DESFire (v{hwMajorVersion})"
        };

        var storageKb = hwStorageSize switch
        {
            0x16 => "2KB",
            0x18 => "4KB",
            0x1A => "8KB",
            0x1C => "16KB",
            0x1E => "32KB",
            _ => "Unknown storage"
        };

        return $"{cardType}, {storageKb} storage";
    }
}
