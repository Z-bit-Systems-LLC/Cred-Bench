using System.Security.Cryptography;
using CredBench.Core.Models;
using CredBench.Core.Models.TechnologyDetails;
using CredBench.Core.Services.Leaf;

namespace CredBench.Core.Services.CardDetectors;

/// <summary>
/// Detects LEAF Verified credentials: MIFARE DUOX cards carrying the LEAF Open Application
/// (AID F51CD6), and JavaCard cards running the emulation applet.
/// <para>
/// The flow follows the LEAF Verified onboarding guide and AsymCred's LeafReader:
/// SelectApplication, ReadData of the certificate in file 2, certificate verification against
/// the trust anchors, then ISOInternalAuthenticate with a fresh challenge verified with the
/// certificate's key.
/// </para>
/// <para>
/// Unlike a production reader, the challenge is sent even when the issuer is untrusted, and every
/// partial failure after a successful SelectApplication is reported in the details rather than as
/// "not detected".
/// </para>
/// </summary>
public class LEAFVerifiedDetector(ILeafTrustStore trustStore, Func<byte[]>? challenge = null) : ICardDetector
{
    public CardTechnology Technology => CardTechnology.LEAFVerified;

    // DESFire SelectApplication F51CD6 (AID LSB first)
    private static readonly byte[] SelectApplication = [0x90, 0x5A, 0x00, 0x00, 0x03, 0xD6, 0x1C, 0xF5, 0x00];
    private static readonly byte[] AdditionalFrame = [0x90, 0xAF, 0x00, 0x00, 0x00];

    // DESFire SelectApplication F51CD5: the NFC Forum NDEF application holding the tap URL
    private static readonly byte[] SelectNdefApplication = [0x90, 0x5A, 0x00, 0x00, 0x03, 0xD5, 0x1C, 0xF5, 0x00];
    private const byte NdefFile = 0x02;
    private const int MaxNdef = 1024;

    private const byte CertificateFile = 0x02;
    private const int HeaderRead = 8;
    private const int ReadChunk = 255;
    private const int MaxCertificate = 1024;
    private const int RndLength = 16;

    private const ushort SwOk = 0x9000;
    private const ushort SwDesfireOk = 0x9100;
    private const ushort SwMoreFrames = 0x91AF;

    private readonly Func<byte[]> _challenge = challenge ?? (() => RandomNumberGenerator.GetBytes(RndLength));

    public (bool Detected, string? Details, object? TypedDetails) Detect(ICardConnection connection)
    {
        var select = connection.Transmit(SelectApplication);
        var selectSw = StatusWord(select);
        if (select.Length != 2 || (selectSw != SwDesfireOk && selectSw != SwOk))
            return (false, null, null);

        LEAFVerifiedDetails details;
        try
        {
            details = ReadCredential(connection);
        }
        catch (Exception ex)
        {
            // A transport failure after a successful select still means the application is present
            details = new LEAFVerifiedDetails { Trust = LeafTrust.Unreadable, Error = ex.Message };
        }

        // Last, because it leaves the NDEF application selected
        details = details with { TapUrl = TryReadTapUrl(connection) };

        var summary = details.OpenId is { } openId
            ? $"LEAF Verified (Open ID {openId}){(details.IsFullyVerified ? "" : ", unverified")}"
            : "LEAF Verified (credential unreadable)";

        return (true, summary, details);
    }

    /// <summary>
    /// Checks a certificate's profile and issuer against the trust anchors. Pure computation,
    /// so it can be re-run when the trust store changes without touching the card.
    /// </summary>
    public static LEAFVerifiedDetails EvaluateCertificate(byte[] certificate, IReadOnlyList<LeafTrustAnchor> anchors)
    {
        LeafCertificate parsed;
        try
        {
            parsed = LeafCertificate.Parse(certificate);
        }
        catch (FormatException ex)
        {
            return new LEAFVerifiedDetails
            {
                Certificate = certificate,
                Trust = LeafTrust.Unreadable,
                Error = ex.Message
            };
        }

        var anchor = anchors.FirstOrDefault(parsed.IsSignedBy);

        return new LEAFVerifiedDetails
        {
            Certificate = certificate,
            OpenId = parsed.OpenId,
            CardPublicKeyHex = parsed.PublicKey is { } key ? Convert.ToHexString(key) : null,
            Issuer = parsed.Issuer,
            Subject = parsed.Subject,
            AuthorityKeyId = parsed.AuthorityKeyId,
            CertificateSerial = parsed.SerialHex,
            ProfileIssues = parsed.ProfileIssues,
            TrustAnchorName = anchor?.Name,
            Trust = parsed.ProfileIssues.Count > 0 ? LeafTrust.ProfileViolation
                : anchor is not null ? LeafTrust.Verified
                : LeafTrust.UntrustedIssuer
        };
    }

    private LEAFVerifiedDetails ReadCredential(ICardConnection connection)
    {
        byte[] certificate;
        try
        {
            certificate = ReadCertificate(connection);
        }
        catch (LeafReadException ex)
        {
            return new LEAFVerifiedDetails
            {
                Trust = LeafTrust.Unreadable,
                Certificate = ex.PartialData,
                Error = ex.Message,
                LastStatusWord = ex.StatusWord is { } sw ? $"{sw:X4}" : null
            };
        }

        var details = EvaluateCertificate(certificate, trustStore.Anchors);

        if (details.CardPublicKeyHex is null)
            return details;

        try
        {
            return details with
            {
                CardSignatureValid = Authenticate(connection, Convert.FromHexString(details.CardPublicKeyHex))
            };
        }
        catch (LeafReadException ex)
        {
            return details with
            {
                Error = ex.Message,
                LastStatusWord = ex.StatusWord is { } sw ? $"{sw:X4}" : null
            };
        }
    }

    /// <summary>
    /// Reads the URI from the NDEF application's file 2 (2-byte NLEN, then the NDEF message).
    /// Each read makes the card generate a new one-time URL, as a phone tap does.
    /// </summary>
    private static string? TryReadTapUrl(ICardConnection connection)
    {
        try
        {
            var select = connection.Transmit(SelectNdefApplication);
            var sw = StatusWord(select);
            if (select.Length != 2 || (sw != SwDesfireOk && sw != SwOk))
                return null;

            var nlenBytes = ReadData(connection, 0, 2, NdefFile);
            var nlen = (nlenBytes[0] << 8) | nlenBytes[1];
            if (nlen is 0 or > MaxNdef)
                return null;

            var message = new byte[nlen];
            for (var read = 0; read < nlen;)
            {
                var chunk = ReadData(connection, 2 + read, Math.Min(nlen - read, ReadChunk), NdefFile);
                chunk.CopyTo(message, read);
                read += chunk.Length;
            }

            return NdefUri.FindUri(message);
        }
        catch (Exception)
        {
            // The tap URL is an extra; failing to read it does not affect the credential
            return null;
        }
    }

    private static byte[] ReadCertificate(ICardConnection connection)
    {
        var header = ReadData(connection, 0, HeaderRead);

        // Certificate length from its DER header: 30 82 HH LL -> 4 + HHLL
        if (header[0] != 0x30 || header[1] != 0x82)
            throw new LeafReadException(
                $"File 2 does not start with a DER certificate: {Convert.ToHexString(header)}", partialData: header);

        var total = 4 + ((header[2] << 8) | header[3]);
        if (total < HeaderRead || total > MaxCertificate)
            throw new LeafReadException($"Implausible certificate length {total}", partialData: header);

        var certificate = new byte[total];
        header.CopyTo(certificate, 0);
        var read = HeaderRead;

        while (read < total)
        {
            byte[] chunk;
            try
            {
                chunk = ReadData(connection, read, Math.Min(total - read, ReadChunk));
            }
            catch (LeafReadException ex)
            {
                throw new LeafReadException(ex.Message, ex.StatusWord, certificate[..read]);
            }

            chunk.CopyTo(certificate, read);
            read += chunk.Length;
        }

        return certificate;
    }

    /// <summary>ReadData of exactly <paramref name="length"/> bytes, following 91 AF continuation frames.</summary>
    private static byte[] ReadData(ICardConnection connection, int offset, int length, byte file = CertificateFile)
    {
        byte[] command =
        [
            0x90, 0xAD, 0x00, 0x00, 0x07, file,
            (byte)offset, (byte)(offset >> 8), (byte)(offset >> 16),
            (byte)length, (byte)(length >> 8), (byte)(length >> 16),
            0x00
        ];

        var data = new List<byte>(length);
        while (true)
        {
            var response = connection.Transmit(command);
            var sw = StatusWord(response);
            if (sw != SwMoreFrames && sw != SwDesfireOk && sw != SwOk)
                throw new LeafReadException(
                    $"ReadData at offset {offset} refused: {DescribeStatusWord(sw)}", sw);

            data.AddRange(response.AsSpan(0, response.Length - 2));
            if (data.Count > length)
                throw new LeafReadException($"Card returned more than the {length} bytes asked for at offset {offset}");

            if (sw != SwMoreFrames)
                break;

            command = AdditionalFrame;
        }

        if (data.Count != length)
            throw new LeafReadException($"Card returned {data.Count} of {length} bytes at offset {offset}");

        return [.. data];
    }

    /// <summary>
    /// ISOInternalAuthenticate: 00 88 00 00 16 80 00 7C 12 81 10 RndA 00, answered by
    /// 7C 54 81 10 RndB 82 40 R||S. The card signs F0 F0 || 80 00 || RndB || RndA.
    /// </summary>
    private bool Authenticate(ICardConnection connection, byte[] cardPublicKey)
    {
        var rndA = _challenge();
        if (rndA.Length != RndLength)
            throw new InvalidOperationException("Challenge must be 16 bytes");

        byte[] command = [0x00, 0x88, 0x00, 0x00, 0x16, 0x80, 0x00, 0x7C, 0x12, 0x81, 0x10, .. rndA, 0x00];

        var response = connection.Transmit(command);
        var sw = StatusWord(response);
        if (sw != SwOk && sw != SwDesfireOk)
            throw new LeafReadException($"ISOInternalAuthenticate refused: {DescribeStatusWord(sw)}", sw);

        if (response.Length >= 22 && response[0] == 0x7C && response[20] == 0x82 && response[21] == 0x44)
            throw new LeafReadException(
                "Card answered with the 68-byte (82 44) signature form, which is not supported yet: " +
                Convert.ToHexString(response));

        if (response.Length != 2 + 0x54 + 2
            || response[0] != 0x7C || response[1] != 0x54
            || response[2] != 0x81 || response[3] != 0x10
            || response[20] != 0x82 || response[21] != 0x40)
            throw new LeafReadException($"Unexpected authenticate response: {Convert.ToHexString(response)}");

        var rndB = response[4..20];
        var signature = response[22..86];
        byte[] message = [0xF0, 0xF0, 0x80, 0x00, .. rndB, .. rndA];

        try
        {
            using var key = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = cardPublicKey[1..33], Y = cardPublicKey[33..65] }
            });
            return key.VerifyData(message, signature, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            // The certificate's point is not on the curve
            return false;
        }
    }

    private static ushort StatusWord(byte[] response) =>
        response.Length < 2 ? (ushort)0 : (ushort)((response[^2] << 8) | response[^1]);

    /// <summary>Names the status words a LEAF Verified read is likely to meet.</summary>
    public static string DescribeStatusWord(ushort sw) => sw switch
    {
        0x91A0 => "91A0 (application not found)",
        0x91BE => "91BE (boundary error)",
        0x91F0 => "91F0 (file not found)",
        0x91AE => "91AE (authentication error)",
        0x91CA => "91CA (command aborted)",
        0x919D => "919D (permission denied)",
        0x917E => "917E (length error)",
        0x911C => "911C (illegal command)",
        0x6A82 => "6A82 (file or application not found)",
        0x6D00 => "6D00 (instruction not supported)",
        0x6E00 => "6E00 (class not supported)",
        0x6982 => "6982 (security status not satisfied)",
        0 => "no status word",
        _ => $"{sw:X4}"
    };

    private sealed class LeafReadException(string message, ushort? statusWord = null, byte[]? partialData = null)
        : Exception(message)
    {
        public ushort? StatusWord { get; } = statusWord;
        public byte[]? PartialData { get; } = partialData;
    }
}
