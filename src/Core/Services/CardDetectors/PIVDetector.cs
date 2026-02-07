using System.Diagnostics;
using CredBench.Core.Models;
using CredBench.Core.Models.TechnologyDetails;

namespace CredBench.Core.Services.CardDetectors;

public class PIVDetector : ICardDetector
{
    public CardTechnology Technology => CardTechnology.PIV;

    // PIV Application AID: A0 00 00 03 08 00 00 10 00 01 00
    private static readonly byte[] PivAid = [0xA0, 0x00, 0x00, 0x03, 0x08, 0x00, 0x00, 0x10, 0x00, 0x01, 0x00];

    // SELECT command: CLA=00, INS=A4, P1=04 (select by name), P2=00
    private static readonly byte[] SelectApduHeader = [0x00, 0xA4, 0x04, 0x00];

    // GET DATA for CHUID - standard PIV variant
    private static readonly byte[] GetChuidCommand =
    [
        0x00, 0xCB, 0x3F, 0xFF, // CLA, INS, P1, P2
        0x05,                   // Lc
        0x5C, 0x03, 0x5F, 0xC1, 0x02, // Tag list for CHUID
        0x00                    // Le
    ];

    // GET DATA for CHUID - alternative variant (different tag list length)
    private static readonly byte[] GetChuidCommandAlt =
    [
        0x00, 0xCB, 0x3F, 0xFF, // CLA, INS, P1, P2
        0x05,                   // Lc
        0x5C, 0x0A, 0x5F, 0xC1, 0x02, // Tag list with extended length
        0x00                    // Le
    ];

    public (bool Detected, string? Details, object? TypedDetails) Detect(ICardConnection connection)
    {
        try
        {
            // Build SELECT PIV AID command
            var command = new byte[SelectApduHeader.Length + 1 + PivAid.Length + 1];
            SelectApduHeader.CopyTo(command, 0);
            command[4] = (byte)PivAid.Length; // Lc
            PivAid.CopyTo(command, 5);
            command[^1] = 0x00; // Le

            var response = connection.Transmit(command);

            if (response.Length >= 2)
            {
                var sw1 = response[^2];
                var sw2 = response[^1];

                // SW 9000 = Success or SW 61xx = More data available
                if ((sw1 == 0x90 && sw2 == 0x00) || sw1 == 0x61)
                {
                    var chuidBytes = TryGetChuid(connection);
                    var (fascn, fascnHex) = chuidBytes != null
                        ? TryParseFascn(chuidBytes)
                        : (null, null);

                    var details = new PIVDetails
                    {
                        Status = "PIV application found",
                        CHUID = chuidBytes != null
                            ? BitConverter.ToString(chuidBytes).Replace("-", " ")
                            : null,
                        FASCN = fascn,
                        FASCNHex = fascnHex,
                    };

                    return (true, "PIV application found", details);
                }
            }

            return (false, null, null);
        }
        catch (Exception ex)
        {
            return (false, $"PIV detection error: {ex.Message}", null);
        }
    }

    private static byte[]? TryGetChuid(ICardConnection connection)
    {
        // Try standard command first, then alternative
        byte[][]  commands = [GetChuidCommand, GetChuidCommandAlt];

        foreach (var command in commands)
        {
            try
            {
                var response = connection.Transmit(command);
                var data = ExtractSuccessData(response);
                if (data != null)
                    return data;
            }
            catch
            {
                // Continue to next command variant
            }
        }

        return null;
    }

    private static byte[]? ExtractSuccessData(byte[] response)
    {
        if (response.Length < 2)
            return null;

        var sw1 = response[^2];
        var sw2 = response[^1];

        // 9000 = Success, 61xx = Success with more data available
        if ((sw1 == 0x90 && sw2 == 0x00) || sw1 == 0x61)
        {
            if (response.Length > 2)
                return response[..^2];
        }

        return null;
    }

    /// <summary>
    /// Parse the CHUID TLV structure to extract the FASC-N (tag 0x30).
    /// Returns the decoded FASC-N string and the raw hex of the FASC-N bytes.
    /// </summary>
    private static (string? Decoded, string? Hex) TryParseFascn(byte[] chuidData)
    {
        try
        {
            if (chuidData.Length < 3)
                return (null, null);

            var index = 0;

            // Skip outer container tag (typically 0x53 for CHUID)
            index++; // skip tag byte

            // Parse BER-TLV length
            index = SkipBerLength(chuidData, index, out var outerLength);
            if (index < 0) return (null, null);

            var endOfData = Math.Min(index + outerLength, chuidData.Length);

            // Search for FASC-N tag (0x30) within the CHUID
            while (index < endOfData - 2)
            {
                var tag = chuidData[index++];

                index = SkipBerLength(chuidData, index, out var length);
                if (index < 0) return (null, null);

                if (tag == 0x30 && length >= 25)
                {
                    // Found FASC-N - extract and decode
                    if (index + length <= endOfData)
                    {
                        var fascnBytes = chuidData[index..(index + length)];
                        var decoded = DecodeFascn(fascnBytes);
                        var hex = BitConverter.ToString(fascnBytes).Replace("-", "");
                        return (decoded, hex);
                    }
                }

                // Skip value field
                index += length;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PIV] FASC-N parse error: {ex.Message}");
        }

        return (null, null);
    }

    private static int SkipBerLength(byte[] data, int index, out int length)
    {
        length = 0;
        if (index >= data.Length)
            return -1;

        var lengthByte = data[index++];

        if ((lengthByte & 0x80) == 0)
        {
            // Short form
            length = lengthByte;
        }
        else
        {
            // Long form
            var numLengthBytes = lengthByte & 0x7F;
            if (index + numLengthBytes > data.Length)
                return -1;

            for (var i = 0; i < numLengthBytes; i++)
                length = (length << 8) | data[index++];
        }

        return index;
    }

    /// <summary>
    /// Decode 200-bit FASC-N data (25 bytes) into the standard FASC-N string.
    /// Each character is 5 bits: 4 data bits (LSB first) + 1 parity bit.
    /// </summary>
    private static string DecodeFascn(byte[] data)
    {
        // 200 bits = 40 five-bit characters
        var chars = new char[40];

        for (var i = 0; i < 40; i++)
        {
            var bitOffset = i * 5;
            var b0 = GetBit(data, bitOffset);
            var b1 = GetBit(data, bitOffset + 1);
            var b2 = GetBit(data, bitOffset + 2);
            var b3 = GetBit(data, bitOffset + 3);
            // bit 4 is parity, skip

            var value = b0 + (b1 << 1) + (b2 << 2) + (b3 << 3);

            chars[i] = value switch
            {
                0 => '0', 1 => '1', 2 => '2', 3 => '3', 4 => '4',
                5 => '5', 6 => '6', 7 => '7', 8 => '8', 9 => '9',
                10 => 'S', // Start Sentinel
                11 or 13 => 'F', // Field Separator
                15 => 'E', // End Sentinel
                _ => '?'
            };
        }

        return new string(chars);
    }

    private static int GetBit(byte[] data, int bitIndex)
    {
        var byteIndex = bitIndex / 8;
        var bitInByte = 7 - (bitIndex % 8); // MSB first

        if (byteIndex >= data.Length)
            return 0;

        return (data[byteIndex] >> bitInByte) & 1;
    }
}
