using CredBench.Core.Models;

namespace CredBench.Core.Services.CardDetectors;

public class PKOCDetector : ICardDetector
{
    public CardTechnology Technology => CardTechnology.PKOC;

    // PSIA PKOC AID: A000000898000001
    private static readonly byte[] PkocAid = [0xA0, 0x00, 0x00, 0x08, 0x98, 0x00, 0x00, 0x01];

    // SELECT command: 00 A4 04 00 08 A000000898000001 00
    private static readonly byte[] SelectPkocCommand =
    [
        0x00, 0xA4, 0x04, 0x00, // CLA, INS, P1, P2
        0x08,                   // Lc (AID length)
        0xA0, 0x00, 0x00, 0x08, 0x98, 0x00, 0x00, 0x01, // AID
        0x00                    // Le
    ];

    // TLV tags per PSIA PKOC spec
    private const byte TagProtocolVersion = 0x5C;

    public (bool Detected, string? Details) Detect(ICardConnection connection)
    {
        try
        {
            // Send SELECT command with PKOC AID
            var response = connection.Transmit(SelectPkocCommand);

            if (!IsSuccess(response))
                return (false, null);

            // Parse response for Protocol Version TLV
            // Expected format: 5C 02 XX XX 90 00
            var data = response[..^2]; // Remove SW1/SW2

            var protocolVersion = ParseProtocolVersion(data);
            if (protocolVersion is null)
                return (false, null);

            var versionString = $"{protocolVersion[0]:X2}.{protocolVersion[1]:X2}";
            return (true, $"PKOC v{versionString}");
        }
        catch
        {
            return (false, null);
        }
    }

    private static byte[]? ParseProtocolVersion(byte[] data)
    {
        // Parse TLV structure to find Protocol Version (tag 0x5C)
        var index = 0;
        while (index < data.Length - 2)
        {
            var tag = data[index];
            var length = data[index + 1];

            if (index + 2 + length > data.Length)
                break;

            if (tag == TagProtocolVersion && length >= 2)
            {
                return [data[index + 2], data[index + 3]];
            }

            index += 2 + length;
        }

        return null;
    }

    private static bool IsSuccess(byte[] response)
    {
        if (response.Length < 2)
            return false;

        var sw1 = response[^2];
        var sw2 = response[^1];

        return sw1 == 0x90 && sw2 == 0x00;
    }
}
