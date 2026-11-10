using System.Diagnostics;
using CredBench.Core.Models;

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
                var details = DESFireVersionDecoder.VersionUnavailable("GetVersion not answered after SELECT");
                return (true, "DESFire card detected (via SELECT), version unknown", details);
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
            if (sw2 == 0xAF)
            {
                Debug.WriteLine("[DESFire] Got 91 AF (more frames)");
                if (response.Length < 9)
                {
                    var shortDetails = DESFireVersionDecoder.VersionUnavailable("GetVersion hardware frame too short");
                    return (true, "DESFire card detected, version unknown", shortDetails);
                }

                var payload = ReadVersionPayload(response, connection, additionalFrameCmd);
                var versionDetails = DESFireVersionDecoder.Decode(payload);
                var versionInfo = $"{versionDetails.CardType}, {versionDetails.StorageSize} storage";
                Debug.WriteLine($"[DESFire] Version: {versionInfo} (GetVersion data {versionDetails.VersionData})");

                return (true, versionInfo, versionDetails);
            }

            // DESFire success: 91 00
            if (sw2 == 0x00)
            {
                Debug.WriteLine("[DESFire] Got 91 00 (success)");
                var details = DESFireVersionDecoder.VersionUnavailable("GetVersion returned no data");
                return (true, "DESFire card detected, version unknown", details);
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
            var errorDetails = DESFireVersionDecoder.VersionUnavailable($"GetVersion rejected: {errorDetail}");
            return (true, $"DESFire card detected ({errorDetail}), version unknown", errorDetails);
        }

        // Some cards return 90 00 with data
        if (sw1 == 0x90 && sw2 == 0x00 && response.Length > 2)
        {
            Debug.WriteLine("[DESFire] Got 90 00 with data");
            var details = DESFireVersionDecoder.VersionUnavailable("GetVersion answered with an ISO status word");
            return (true, "DESFire card detected, version unknown", details);
        }

        return (false, null, null);
    }

    /// <summary>
    /// Collects the GetVersion payload: the first frame plus up to two AdditionalFrame responses,
    /// with status words removed. Stops early if a frame fails, so a partial payload is still
    /// decoded from what arrived.
    /// </summary>
    private static byte[] ReadVersionPayload(byte[] firstResponse, ICardConnection connection, byte[] additionalFrameCmd)
    {
        List<byte> payload = [.. firstResponse[..^2]];
        var response = firstResponse;

        for (var frame = 2; frame <= 3 && response is [.., 0x91, 0xAF]; frame++)
        {
            try
            {
                Debug.WriteLine($"[DESFire] AdditionalFrame: {BitConverter.ToString(additionalFrameCmd)}");
                response = connection.Transmit(additionalFrameCmd);
                Debug.WriteLine($"[DESFire] Frame {frame}: {BitConverter.ToString(response)}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DESFire] Error getting frame {frame}: {ex.Message}");
                break;
            }

            if (response is not [.., 0x91, 0xAF or 0x00])
            {
                break;
            }

            payload.AddRange(response[..^2]);
        }

        return [.. payload];
    }
}
