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

    // GET DATA for CHUID: 00 CB 3F FF 05 5C 03 5F C1 02 00
    private static readonly byte[] GetChuidCommand =
    [
        0x00, 0xCB, 0x3F, 0xFF, // CLA, INS, P1, P2
        0x05,                   // Lc
        0x5C, 0x03, 0x5F, 0xC1, 0x02, // Tag list for CHUID
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
                    var details = new PIVDetails
                    {
                        Status = "PIV application found",
                        CHUID = TryGetChuid(connection),
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

    private static string? TryGetChuid(ICardConnection connection)
    {
        try
        {
            var response = connection.Transmit(GetChuidCommand);

            if (response.Length >= 2)
            {
                var sw1 = response[^2];
                var sw2 = response[^1];

                if (sw1 == 0x90 && sw2 == 0x00 && response.Length > 2)
                {
                    var data = response[..^2];
                    return BitConverter.ToString(data).Replace("-", " ");
                }
            }
        }
        catch
        {
            // CHUID may not be readable without authentication
        }

        return null;
    }
}
