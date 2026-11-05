using System.Security.Cryptography;
using CredBench.Core.Services;

namespace CredBench.Core.Tests.Leaf;

/// <summary>
/// Behaves like LeafOpenApplet (src/Applet/src/com/zbitsystems/leaf): an on-card P-256 key,
/// personalisation (GET PUBLIC KEY / PUT CERTIFICATE / LOCK), then the reader side once locked.
/// </summary>
internal sealed class FakeLeafApplet : ICardConnection
{
    private const int FrameSize = 59;

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] _certificate = new byte[1024];
    private readonly Queue<byte[]> _pendingFrames = new();
    private int _certificateLength;
    private bool _openAppSelected;

    public bool Locked { get; private set; }

    /// <summary>When set, LOCK answers 6984 regardless of the certificate.</summary>
    public bool RejectLock { get; set; }

    public byte[] PublicKey
    {
        get
        {
            var q = _key.ExportParameters(false).Q;
            return [0x04, .. q.X!, .. q.Y!];
        }
    }

    public string ReaderName => "Fake applet";
    public string? GetATR() => null;
    public string? GetUID() => null;
    public string? GetProtocol() => "T1";

    public byte[] Transmit(byte[] command)
    {
        switch (command[0], command[1])
        {
            case (0x00, 0xA4):
                return [0x90, 0x00];

            case (0x80, 0x10):
                return Locked ? [0x69, 0x85] : [.. PublicKey, 0x90, 0x00];

            case (0x80, 0x12):
            {
                if (Locked) return [0x69, 0x85];
                var offset = (command[2] << 8) | command[3];
                var length = command[4];
                if (offset + length > _certificate.Length) return [0x6A, 0x84];
                Buffer.BlockCopy(command, 5, _certificate, offset, length);
                _certificateLength = Math.Max(_certificateLength, offset + length);
                return [0x90, 0x00];
            }

            case (0x80, 0x14):
            {
                if (Locked) return [0x69, 0x85];
                var length = (command[5] << 8) | command[6];
                if (RejectLock || length != _certificateLength) return [0x69, 0x84];
                Locked = true;
                return [0x90, 0x00];
            }

            case (0x90, 0x5A):
                _openAppSelected = Locked && command[5..8].SequenceEqual(new byte[] { 0xD6, 0x1C, 0xF5 });
                return _openAppSelected ? [0x91, 0x00] : [0x91, 0xA0];

            case (0x90, 0xAD) when _openAppSelected:
            {
                var offset = command[6] | (command[7] << 8) | (command[8] << 16);
                var length = command[9] | (command[10] << 8) | (command[11] << 16);
                if (offset + length > _certificateLength) return [0x91, 0xBE];
                _pendingFrames.Clear();
                for (var i = offset; i < offset + length; i += FrameSize)
                    _pendingFrames.Enqueue(_certificate[i..Math.Min(i + FrameSize, offset + length)]);
                return NextFrame();
            }

            case (0x90, 0xAF):
                return _pendingFrames.Count > 0 ? NextFrame() : [0x91, 0xCA];

            case (0x00, 0x88) when _openAppSelected:
            {
                var rndA = command[11..27];
                var rndB = RandomNumberGenerator.GetBytes(16);
                byte[] message = [0xF0, 0xF0, 0x80, 0x00, .. rndB, .. rndA];
                var signature = _key.SignData(message, HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
                return [0x7C, 0x54, 0x81, 0x10, .. rndB, 0x82, 0x40, .. signature, 0x90, 0x00];
            }

            default:
                return [0x91, 0x1C];
        }
    }

    private byte[] NextFrame()
    {
        var frame = _pendingFrames.Count > 0 ? _pendingFrames.Dequeue() : [];
        byte[] sw = _pendingFrames.Count > 0 ? [0x91, 0xAF] : [0x91, 0x00];
        return [.. frame, .. sw];
    }

    public void Dispose() => _key.Dispose();
}
