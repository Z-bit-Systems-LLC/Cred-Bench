using CredBench.Core.Services;

namespace CredBench.Core.Tests.Leaf;

/// <summary>
/// A scripted LEAF Verified card: answers SelectApplication, serves file 2 the way a DESFire
/// card does (59-byte frames chained with 91 AF), and answers ISOInternalAuthenticate with the
/// vector RndB and signature. Every C-APDU is logged. Behaviour can be overridden per test.
/// </summary>
internal sealed class FakeLeafCard : ICardConnection
{
    private const int FrameSize = 59;
    private readonly Queue<byte[]> _pendingFrames = new();

    public List<byte[]> Commands { get; } = [];

    private bool _ndefSelected;

    /// <summary>File 2 of the NDEF application (2-byte NLEN + message), or null for a card without one.</summary>
    public byte[]? NdefFile { get; set; }

    public byte[] File2 { get; set; } = LeafVectors.CardCert;
    public byte[] SelectResponse { get; set; } = [0x91, 0x00];
    public byte[] RndB { get; set; } = LeafVectors.RndB;
    public byte[] CardSignature { get; set; } = LeafVectors.CardSig;

    /// <summary>When set, replaces the whole authenticate response.</summary>
    public byte[]? AuthenticateResponse { get; set; }

    /// <summary>When set, ReadData at this offset answers with this status word.</summary>
    public (int Offset, byte[] Sw)? ReadError { get; set; }

    /// <summary>Extra bytes appended to the last frame of every ReadData.</summary>
    public int OverDeliver { get; set; }

    /// <summary>Bytes withheld from every ReadData.</summary>
    public int UnderDeliver { get; set; }

    public string ReaderName => "Fake";
    public string? GetATR() => null;
    public string? GetUID() => null;
    public string? GetProtocol() => "T1";

    public IEnumerable<byte[]> ReadDataCommands => Commands.TakeWhile(c => !IsNdefSelect(c))
        .Where(c => c[0] == 0x90 && c[1] == 0xAD);

    private static bool IsNdefSelect(byte[] c) => c[1] == 0x5A && c[5] == 0xD5;

    /// <summary>Builds an NDEF file (NLEN + one short URI record, https:// prefix).</summary>
    public static byte[] NdefFileWithUri(string httpsUrlWithoutPrefix)
    {
        var uri = System.Text.Encoding.ASCII.GetBytes(httpsUrlWithoutPrefix);
        byte[] record = [0xD1, 0x01, (byte)(uri.Length + 1), (byte)'U', 0x04, .. uri];
        return [(byte)(record.Length >> 8), (byte)record.Length, .. record, .. new byte[16]];
    }

    public byte[] Transmit(byte[] command)
    {
        Commands.Add(command);

        switch (command[0], command[1])
        {
            case (0x90, 0x5A):
                _ndefSelected = command[5..8].SequenceEqual(new byte[] { 0xD5, 0x1C, 0xF5 });
                if (_ndefSelected)
                    return NdefFile is null ? [0x91, 0xA0] : [0x91, 0x00];
                return SelectResponse;

            case (0x90, 0xAD) when _ndefSelected:
            {
                var offset = command[6] | (command[7] << 8) | (command[8] << 16);
                var length = command[9] | (command[10] << 8) | (command[11] << 16);
                if (offset + length > NdefFile!.Length)
                    return [0x91, 0xBE];
                _pendingFrames.Clear();
                _pendingFrames.Enqueue(NdefFile[offset..(offset + length)]);
                return NextFrame();
            }

            case (0x90, 0xAD):
            {
                var offset = command[6] | (command[7] << 8) | (command[8] << 16);
                var length = command[9] | (command[10] << 8) | (command[11] << 16);
                if (ReadError is { } error && error.Offset == offset)
                    return error.Sw;
                if (offset + length > File2.Length)
                    return [0x91, 0xBE];

                var data = File2[offset..(offset + length)];
                if (OverDeliver > 0) data = [.. data, .. new byte[OverDeliver]];
                if (UnderDeliver > 0) data = data[..^UnderDeliver];

                _pendingFrames.Clear();
                for (var i = 0; i < data.Length; i += FrameSize)
                    _pendingFrames.Enqueue(data[i..Math.Min(i + FrameSize, data.Length)]);
                if (_pendingFrames.Count == 0)
                    _pendingFrames.Enqueue([]);
                return NextFrame();
            }

            case (0x90, 0xAF):
                return _pendingFrames.Count > 0 ? NextFrame() : [0x91, 0xCA];

            case (0x00, 0x88):
                return AuthenticateResponse ??
                    [0x7C, 0x54, 0x81, 0x10, .. RndB, 0x82, 0x40, .. CardSignature, 0x90, 0x00];

            default:
                return [0x91, 0x1C];
        }
    }

    private byte[] NextFrame()
    {
        var frame = _pendingFrames.Dequeue();
        byte[] sw = _pendingFrames.Count > 0 ? [0x91, 0xAF] : [0x91, 0x00];
        return [.. frame, .. sw];
    }

    public void Dispose()
    {
    }
}
