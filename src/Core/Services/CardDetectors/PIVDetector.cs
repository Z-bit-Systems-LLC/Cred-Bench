using CredBench.Core.Models;

namespace CredBench.Core.Services.CardDetectors;

public class PIVDetector : ICardDetector
{
    public CardTechnology Technology => CardTechnology.PIV;

    // PIV Application AID: A0 00 00 03 08 00 00 10 00 01 00
    private static readonly byte[] PivAid = [0xA0, 0x00, 0x00, 0x03, 0x08, 0x00, 0x00, 0x10, 0x00, 0x01, 0x00];

    // SELECT command: CLA=00, INS=A4, P1=04 (select by name), P2=00
    private static readonly byte[] SelectApduHeader = [0x00, 0xA4, 0x04, 0x00];

    public (bool Detected, string? Details) Detect(ICardConnection connection)
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

                // SW 9000 = Success
                if (sw1 == 0x90 && sw2 == 0x00)
                {
                    return (true, "PIV application found");
                }

                // SW 61xx = More data available (also success)
                if (sw1 == 0x61)
                {
                    return (true, "PIV application found (more data available)");
                }
            }

            return (false, null);
        }
        catch (Exception ex)
        {
            return (false, $"PIV detection error: {ex.Message}");
        }
    }
}
