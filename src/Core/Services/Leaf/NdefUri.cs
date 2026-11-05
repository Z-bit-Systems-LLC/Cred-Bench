using System.Text;

namespace CredBench.Core.Services.Leaf;

/// <summary>
/// Minimal NDEF reader: finds the first URI record (TNF well-known, type "U") in an NDEF message.
/// </summary>
public static class NdefUri
{
    // NFC Forum URI Record Type Definition, identifier codes 0x00-0x23
    private static readonly string[] Prefixes =
    [
        "", "http://www.", "https://www.", "http://", "https://", "tel:", "mailto:",
        "ftp://anonymous:anonymous@", "ftp://ftp.", "ftps://", "sftp://", "smb://", "nfs://",
        "ftp://", "dav://", "news:", "telnet://", "imap:", "rtsp://", "urn:", "pop:", "sip:",
        "sips:", "tftp:", "btspp://", "btl2cap://", "btgoep://", "tcpobex://", "irdaobex://",
        "file://", "urn:epc:id:", "urn:epc:tag:", "urn:epc:pat:", "urn:epc:raw:", "urn:epc:",
        "urn:nfc:"
    ];

    /// <summary>Returns the first URI in the message, or null if there is none or the message is malformed.</summary>
    public static string? FindUri(ReadOnlySpan<byte> message)
    {
        var pos = 0;
        while (pos < message.Length)
        {
            var header = message[pos++];
            var tnf = header & 0x07;
            var shortRecord = (header & 0x10) != 0;
            var hasId = (header & 0x08) != 0;
            var last = (header & 0x40) != 0;

            if (pos >= message.Length) return null;
            int typeLength = message[pos++];

            int payloadLength;
            if (shortRecord)
            {
                if (pos >= message.Length) return null;
                payloadLength = message[pos++];
            }
            else
            {
                if (pos + 4 > message.Length) return null;
                payloadLength = (message[pos] << 24) | (message[pos + 1] << 16) | (message[pos + 2] << 8) | message[pos + 3];
                pos += 4;
            }

            var idLength = 0;
            if (hasId)
            {
                if (pos >= message.Length) return null;
                idLength = message[pos++];
            }

            if (payloadLength < 0 || pos + typeLength + idLength + payloadLength > message.Length)
                return null;

            var type = message.Slice(pos, typeLength);
            var payload = message.Slice(pos + typeLength + idLength, payloadLength);
            pos += typeLength + idLength + payloadLength;

            if (tnf == 0x01 && type.Length == 1 && type[0] == (byte)'U' && payload.Length >= 1)
            {
                var prefix = payload[0] < Prefixes.Length ? Prefixes[payload[0]] : "";
                return prefix + Encoding.UTF8.GetString(payload[1..]);
            }

            if (last) break;
        }

        return null;
    }
}
