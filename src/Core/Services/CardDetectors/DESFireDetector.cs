using System.Diagnostics;
using CredBench.Core.Models;
using CredBench.Core.Models.TechnologyDetails;

namespace CredBench.Core.Services.CardDetectors;

public class DESFireDetector : ICardDetector
{
    public CardTechnology Technology => CardTechnology.DESFire;

    // Various DESFire AIDs to try
    private static readonly byte[][] DesfireAids =
    [
        [0xD2, 0x76, 0x00, 0x00, 0x85, 0x01, 0x00], // DESFire standard
        [0xD2, 0x76, 0x00, 0x00, 0x85, 0x01, 0x01], // DESFire EV1
        [0xD2, 0x76, 0x00, 0x00, 0x85, 0x01, 0x02], // DESFire EV2
    ];

    // GetVersion commands in different formats
    // Native wrapped: CLA=90, INS=60, P1=00, P2=00, Le=00
    private static readonly byte[] GetVersionNativeWrapped = [0x90, 0x60, 0x00, 0x00, 0x00];

    // ISO wrapped: CLA=00, INS=60, P1=00, P2=00, Le=00 (some cards need this)
    private static readonly byte[] GetVersionIsoWrapped = [0x00, 0x60, 0x00, 0x00, 0x00];

    // Raw native command (just the command byte)
    private static readonly byte[] GetVersionRaw = [0x60];

    // Additional Frames commands
    private static readonly byte[] AdditionalFrameNative = [0x90, 0xAF, 0x00, 0x00, 0x00];
    private static readonly byte[] AdditionalFrameIso = [0x00, 0xAF, 0x00, 0x00, 0x00];

    public (bool Detected, string? Details, object? TypedDetails) Detect(ICardConnection connection)
    {
        Debug.WriteLine("=== DESFire Detection Started ===");

        // Get ATR for logging
        var atr = connection.GetATR();
        Debug.WriteLine($"[DESFire] ATR: {atr ?? "(null)"}");

        // Try multiple detection strategies

        // Strategy 1: Try SELECT with various DESFire AIDs
        foreach (var aid in DesfireAids)
        {
            if (TrySelectAid(connection, aid))
            {
                // AID selected successfully - try GetVersion
                var result = TryGetVersion(connection);
                if (result.Detected)
                    return result;

                // Even if GetVersion failed, SELECT succeeded so it's likely DESFire
                Debug.WriteLine("[DESFire] SELECT succeeded - reporting as DESFire");
                var details = new DESFireDetails { CardType = "DESFire" };
                return (true, "DESFire card detected (via SELECT)", details);
            }
        }

        // Strategy 2: Try GetVersion with native wrapped format (without SELECT)
        Debug.WriteLine("[DESFire] Trying GetVersion without SELECT...");
        var versionResult = TryGetVersion(connection);
        if (versionResult.Detected)
            return versionResult;

        // Strategy 3: Try GetVersion with ISO wrapped format
        Debug.WriteLine("[DESFire] Trying ISO wrapped GetVersion...");
        versionResult = TryGetVersionIso(connection);
        if (versionResult.Detected)
            return versionResult;

        // Strategy 4: Try raw GetVersion command
        Debug.WriteLine("[DESFire] Trying raw GetVersion command...");
        versionResult = TryGetVersionRaw(connection);
        if (versionResult.Detected)
            return versionResult;

        Debug.WriteLine("[DESFire] All detection strategies failed - not a DESFire card");
        return (false, null, null);
    }

    private static bool TrySelectAid(ICardConnection connection, byte[] aid)
    {
        try
        {
            // Build SELECT command: 00 A4 04 00 [Lc] [AID] 00
            var command = new byte[6 + aid.Length];
            command[0] = 0x00; // CLA
            command[1] = 0xA4; // INS (SELECT)
            command[2] = 0x04; // P1 (Select by DF name)
            command[3] = 0x00; // P2
            command[4] = (byte)aid.Length; // Lc
            Array.Copy(aid, 0, command, 5, aid.Length);
            command[^1] = 0x00; // Le

            Debug.WriteLine($"[DESFire] SELECT AID {BitConverter.ToString(aid)}: {BitConverter.ToString(command)}");
            var response = connection.Transmit(command);
            Debug.WriteLine($"[DESFire] SELECT response: {BitConverter.ToString(response)}");

            if (response.Length >= 2)
            {
                var sw1 = response[^2];
                var sw2 = response[^1];

                // 90 00 = Success, 61 xx = Success with more data, 91 00 = DESFire success
                if ((sw1 == 0x90 && sw2 == 0x00) || sw1 == 0x61 || (sw1 == 0x91 && sw2 == 0x00))
                {
                    Debug.WriteLine($"[DESFire] SELECT succeeded (SW={sw1:X2}{sw2:X2})");
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DESFire] SELECT exception: {ex.Message}");
            return false;
        }
    }

    private (bool Detected, string? Details, object? TypedDetails) TryGetVersion(ICardConnection connection)
    {
        try
        {
            Debug.WriteLine($"[DESFire] GetVersion (native): {BitConverter.ToString(GetVersionNativeWrapped)}");
            var response = connection.Transmit(GetVersionNativeWrapped);
            Debug.WriteLine($"[DESFire] Response: {BitConverter.ToString(response)}");

            return ParseGetVersionResponse(response, connection, AdditionalFrameNative);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DESFire] GetVersion exception: {ex.Message}");
            return (false, null, null);
        }
    }

    private (bool Detected, string? Details, object? TypedDetails) TryGetVersionIso(ICardConnection connection)
    {
        try
        {
            Debug.WriteLine($"[DESFire] GetVersion (ISO): {BitConverter.ToString(GetVersionIsoWrapped)}");
            var response = connection.Transmit(GetVersionIsoWrapped);
            Debug.WriteLine($"[DESFire] Response: {BitConverter.ToString(response)}");

            return ParseGetVersionResponse(response, connection, AdditionalFrameIso);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DESFire] GetVersion ISO exception: {ex.Message}");
            return (false, null, null);
        }
    }

    private (bool Detected, string? Details, object? TypedDetails) TryGetVersionRaw(ICardConnection connection)
    {
        try
        {
            Debug.WriteLine($"[DESFire] GetVersion (raw): {BitConverter.ToString(GetVersionRaw)}");
            var response = connection.Transmit(GetVersionRaw);
            Debug.WriteLine($"[DESFire] Response: {BitConverter.ToString(response)}");

            return ParseGetVersionResponse(response, connection, AdditionalFrameNative);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DESFire] GetVersion raw exception: {ex.Message}");
            return (false, null, null);
        }
    }

    private (bool Detected, string? Details, object? TypedDetails) ParseGetVersionResponse(
        byte[] response, ICardConnection connection, byte[] additionalFrameCmd)
    {
        if (response.Length < 2)
            return (false, null, null);

        var sw1 = response[^2];
        var sw2 = response[^1];
        Debug.WriteLine($"[DESFire] Status: SW1={sw1:X2}, SW2={sw2:X2}");

        // Any 91 xx response is a DESFire card (native command understood)
        if (sw1 == 0x91)
        {
            // DESFire success with more frames: 91 AF
            if (sw2 == 0xAF && response.Length >= 9)
            {
                Debug.WriteLine("[DESFire] Got 91 AF (more frames)");
                var (versionInfo, typedDetails) = ParseVersionInfo(response);
                Debug.WriteLine($"[DESFire] Version: {versionInfo}");

                // Get remaining frames
                GetRemainingVersionFrames(connection, additionalFrameCmd);

                return (true, versionInfo, typedDetails);
            }

            // DESFire success: 91 00
            if (sw2 == 0x00)
            {
                Debug.WriteLine("[DESFire] Got 91 00 (success)");
                var details = new DESFireDetails { CardType = "DESFire" };
                return (true, "DESFire card detected", details);
            }

            // DESFire error codes - card is DESFire but operation denied
            var errorDetail = sw2 switch
            {
                0x0B => "authentication required",
                0x0C => "additional frame expected",
                0x0E => "out of EEPROM",
                0x1C => "illegal command",
                0x1E => "integrity error",
                0x40 => "no such key",
                0x7E => "length error",
                0x9D => "permission denied",
                0x9E => "parameter error",
                0xA0 => "application not found",
                0xAE => "authentication error",
                0xBE => "boundary error",
                0xC1 => "card integrity error",
                0xCA => "command aborted",
                0xCD => "card disabled",
                0xCE => "count error",
                0xDE => "duplicate error",
                0xEE => "EEPROM error",
                0xF0 => "file not found",
                0xF1 => "file integrity error",
                _ => $"error 0x{sw2:X2}"
            };

            Debug.WriteLine($"[DESFire] Got 91 {sw2:X2} ({errorDetail})");
            var errorDetails = new DESFireDetails { CardType = "DESFire" };
            return (true, $"DESFire card detected ({errorDetail})", errorDetails);
        }

        // Some cards return 90 00 with data
        if (sw1 == 0x90 && sw2 == 0x00 && response.Length > 2)
        {
            Debug.WriteLine("[DESFire] Got 90 00 with data");
            var details = new DESFireDetails { CardType = "DESFire" };
            return (true, "DESFire card detected", details);
        }

        return (false, null, null);
    }

    private static void GetRemainingVersionFrames(ICardConnection connection, byte[] additionalFrameCmd)
    {
        try
        {
            Debug.WriteLine($"[DESFire] AdditionalFrame: {BitConverter.ToString(additionalFrameCmd)}");
            var response2 = connection.Transmit(additionalFrameCmd);
            Debug.WriteLine($"[DESFire] Frame 2: {BitConverter.ToString(response2)}");

            if (response2.Length >= 2 && response2[^2] == 0x91 && response2[^1] == 0xAF)
            {
                var response3 = connection.Transmit(additionalFrameCmd);
                Debug.WriteLine($"[DESFire] Frame 3: {BitConverter.ToString(response3)}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DESFire] Error getting frames: {ex.Message}");
        }
    }

    private static (string Info, DESFireDetails Details) ParseVersionInfo(byte[] response)
    {
        if (response.Length < 9)
        {
            var defaultDetails = new DESFireDetails { CardType = "DESFire" };
            return ("DESFire card detected", defaultDetails);
        }

        // GetVersion response frame 1 (hardware info):
        // Byte 0: Vendor ID (0x04 = NXP)
        // Byte 1: Type
        // Byte 2: Subtype
        // Byte 3: Major version
        // Byte 4: Minor version
        // Byte 5: Storage size
        // Byte 6: Protocol
        // Bytes 7-8: Status (91 AF)
        var hwMajorVersion = response[3];
        var hwMinorVersion = response[4];
        var hwStorageSize = response[5];

        var cardType = hwMajorVersion switch
        {
            0x00 => "DESFire",
            0x01 => "DESFire EV1",
            0x12 => "DESFire EV2",
            0x33 => "DESFire EV3",
            _ => $"DESFire (v{hwMajorVersion})"
        };

        var storageKb = hwStorageSize switch
        {
            0x16 => "2KB",
            0x18 => "4KB",
            0x1A => "8KB",
            0x1C => "16KB",
            0x1E => "32KB",
            _ => "Unknown"
        };

        var details = new DESFireDetails
        {
            CardType = cardType,
            HardwareVersion = $"{hwMajorVersion}.{hwMinorVersion}",
            StorageSize = storageKb
        };

        return ($"{cardType}, {storageKb} storage", details);
    }

}
