using CredBench.Core.Models;

namespace CredBench.Core.Services.CardDetectors;

public class PKOCDetector : ICardDetector
{
    public CardTechnology Technology => CardTechnology.PKOC;

    // PKOC uses NDEF (NFC Data Exchange Format) on NFC Type 4 tags
    // NDEF Application AID: D2760000850101
    private static readonly byte[] NdefAid = [0xD2, 0x76, 0x00, 0x00, 0x85, 0x01, 0x01];

    // SELECT command header
    private static readonly byte[] SelectApduHeader = [0x00, 0xA4, 0x04, 0x00];

    // PKOC NDEF record type
    private const string PkocRecordType = "pkoc.hid.com";

    public (bool Detected, string? Details) Detect(ICardConnection connection)
    {
        try
        {
            // Step 1: Select NDEF Application
            var selectNdef = BuildSelectCommand(NdefAid);
            var response = connection.Transmit(selectNdef);

            if (!IsSuccess(response))
                return (false, null);

            // Step 2: Select Capability Container (CC) file
            byte[] selectCc = [0x00, 0xA4, 0x00, 0x0C, 0x02, 0xE1, 0x03];
            response = connection.Transmit(selectCc);

            if (!IsSuccess(response))
                return (false, null);

            // Step 3: Read Capability Container
            byte[] readCc = [0x00, 0xB0, 0x00, 0x00, 0x0F];
            response = connection.Transmit(readCc);

            if (!IsSuccess(response) || response.Length < 17)
                return (false, null);

            // Parse CC to get NDEF file ID (bytes 9-10)
            var ndefFileId = new byte[] { response[9], response[10] };

            // Step 4: Select NDEF file
            var selectNdefFile = new byte[] { 0x00, 0xA4, 0x00, 0x0C, 0x02, ndefFileId[0], ndefFileId[1] };
            response = connection.Transmit(selectNdefFile);

            if (!IsSuccess(response))
                return (false, null);

            // Step 5: Read NDEF length
            byte[] readLength = [0x00, 0xB0, 0x00, 0x00, 0x02];
            response = connection.Transmit(readLength);

            if (!IsSuccess(response) || response.Length < 4)
                return (false, null);

            var ndefLength = (response[0] << 8) | response[1];
            if (ndefLength == 0 || ndefLength > 1024)
                return (false, null);

            // Step 6: Read NDEF message
            var readNdef = new byte[] { 0x00, 0xB0, 0x00, 0x02, (byte)Math.Min(ndefLength, 255) };
            response = connection.Transmit(readNdef);

            if (!IsSuccess(response))
                return (false, null);

            // Check if NDEF contains PKOC signature
            var ndefData = response[..^2];
            if (ContainsPkocSignature(ndefData))
            {
                return (true, "PKOC credential detected via NDEF");
            }

            return (false, null);
        }
        catch (Exception ex)
        {
            return (false, $"PKOC detection error: {ex.Message}");
        }
    }

    private static byte[] BuildSelectCommand(byte[] aid)
    {
        var command = new byte[SelectApduHeader.Length + 1 + aid.Length + 1];
        SelectApduHeader.CopyTo(command, 0);
        command[4] = (byte)aid.Length;
        aid.CopyTo(command, 5);
        command[^1] = 0x00;
        return command;
    }

    private static bool IsSuccess(byte[] response)
    {
        if (response.Length < 2)
            return false;

        var sw1 = response[^2];
        var sw2 = response[^1];

        return sw1 == 0x90 && sw2 == 0x00;
    }

    private static bool ContainsPkocSignature(byte[] ndefData)
    {
        if (ndefData.Length < 10)
            return false;

        // Look for NDEF External Record with PKOC type
        var index = 0;
        while (index < ndefData.Length - 3)
        {
            var flags = ndefData[index];
            var tnf = flags & 0x07; // Type Name Format

            if (tnf == 0x04) // NFC Forum external type
            {
                var typeLength = ndefData[index + 1];
                var typeOffset = (flags & 0x10) != 0 ? index + 3 : index + 6;
                if (typeOffset + typeLength <= ndefData.Length)
                {
                    var type = System.Text.Encoding.ASCII.GetString(
                        ndefData, typeOffset, typeLength);

                    if (type.Contains(PkocRecordType, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            index++;
        }

        return false;
    }
}
