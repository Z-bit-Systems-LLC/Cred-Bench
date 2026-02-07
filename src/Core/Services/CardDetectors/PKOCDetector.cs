using System.Security.Cryptography;
using CredBench.Core.Models;
using CredBench.Core.Models.TechnologyDetails;

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

    // TLV tags per PSIA PKOC 1.1 spec
    private const byte TagProtocolVersion = 0x5C;
    private const byte TagPublicKey = 0x5A;

    public (bool Detected, string? Details, object? TypedDetails) Detect(ICardConnection connection)
    {
        try
        {
            // Step 1: SELECT PKOC applet
            var response = connection.Transmit(SelectPkocCommand);

            if (!IsSuccess(response))
                return (false, null, null);

            var data = response[..^2]; // Remove SW1/SW2

            var protocolVersion = ParseProtocolVersion(data);
            if (protocolVersion is null)
                return (false, null, null);

            var versionString = $"{protocolVersion[0]:X2}.{protocolVersion[1]:X2}";

            // Step 2: AUTHENTICATE to retrieve public key
            var publicKey = TryGetPublicKey(connection, protocolVersion);

            var details = new PKOCDetails
            {
                ProtocolVersion = versionString,
                PublicKeyHex = publicKey != null
                    ? BitConverter.ToString(publicKey).Replace("-", "")
                    : null,
            };

            return (true, $"PKOC v{versionString}", details);
        }
        catch
        {
            return (false, null, null);
        }
    }

    private static byte[]? TryGetPublicKey(ICardConnection connection, byte[] protocolVersion)
    {
        try
        {
            // Build AUTHENTICATE command per PSIA PKOC 1.1 spec:
            // CLA=80, INS=80, P1=00, P2=01, Lc=38
            // Data: 4C 10 [16-byte Transaction ID] 5C 02 [Protocol Version] 4D 20 [32-byte Reader ID]
            // Le=00

            var transactionId = RandomNumberGenerator.GetBytes(16);
            var readerIdentifier = new byte[32]; // zeros for identification-only mode

            var commandData = new byte[56]; // 0x38
            var pos = 0;

            // Transaction ID TLV: 4C 10 [16 bytes]
            commandData[pos++] = 0x4C;
            commandData[pos++] = 0x10;
            Array.Copy(transactionId, 0, commandData, pos, 16);
            pos += 16;

            // Protocol Version TLV: 5C 02 [2 bytes]
            commandData[pos++] = 0x5C;
            commandData[pos++] = 0x02;
            commandData[pos++] = protocolVersion[0];
            commandData[pos++] = protocolVersion[1];

            // Reader Identifier TLV: 4D 20 [32 bytes]
            commandData[pos++] = 0x4D;
            commandData[pos++] = 0x20;
            Array.Copy(readerIdentifier, 0, commandData, pos, 32);

            // Build full APDU: 80 80 00 01 38 [data] 00
            var command = new byte[5 + commandData.Length + 1];
            command[0] = 0x80; // CLA
            command[1] = 0x80; // INS (AUTHENTICATE)
            command[2] = 0x00; // P1
            command[3] = 0x01; // P2
            command[4] = (byte)commandData.Length; // Lc
            Array.Copy(commandData, 0, command, 5, commandData.Length);
            command[^1] = 0x00; // Le

            var response = connection.Transmit(command);

            if (!IsSuccess(response))
                return null;

            var responseData = response[..^2];

            // Parse TLV for public key (tag 0x5A, length 0x41 = 65 bytes)
            return ParseTlvValue(responseData, TagPublicKey);
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? ParseTlvValue(byte[] data, byte targetTag)
    {
        var index = 0;
        while (index < data.Length - 2)
        {
            var tag = data[index++];
            var length = data[index++];

            if (index + length > data.Length)
                break;

            if (tag == targetTag)
                return data[index..(index + length)];

            index += length;
        }

        return null;
    }

    private static byte[]? ParseProtocolVersion(byte[] data)
    {
        var result = ParseTlvValue(data, TagProtocolVersion);
        return result is { Length: >= 2 } ? result[..2] : null;
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
