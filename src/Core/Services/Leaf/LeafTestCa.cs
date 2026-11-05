using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CredBench.Core.Services.Leaf;

/// <summary>
/// A LEAF Verified test root: a self-signed P-256 CA that issues card certificates for test cards.
/// Uses the same files as AsymCred's <c>leaf-tool</c> (<c>ca.key</c> as unencrypted PKCS#8 PEM,
/// <c>ca.crt</c> as PEM), so one test root serves both tools.
/// </summary>
/// <remarks>
/// Card certificates follow <c>leaf-tool</c>: subject is a single serialNumber holding the 12-digit
/// Open ID, basicConstraints (critical, not a CA), keyUsage digitalSignature (critical), an authority
/// key identifier, and no expiry (99991231235959Z).
/// </remarks>
public sealed class LeafTestCa : IDisposable
{
    public const string KeyFileName = "ca.key";
    public const string CertificateFileName = "ca.crt";
    /// <summary>
    /// The certificate itself says it is fake, so the status travels with it: the trust anchor list,
    /// every card certificate's issuer, and any reader log that records the issuer all show it.
    /// </summary>
    public const string DefaultName = "FAKE LEAF Test Root - NOT FOR PRODUCTION";

    /// <summary>Organization of every test root, whatever its common name.</summary>
    public const string Organization = "Z-bit Systems (TEST ONLY)";

    private const string SerialNumberOid = "2.5.4.5";
    private static readonly DateTimeOffset NoExpiry = new(9999, 12, 31, 23, 59, 59, TimeSpan.Zero);

    private readonly ECDsa _key;

    private LeafTestCa(ECDsa key, X509Certificate2 certificate)
    {
        _key = key;
        Certificate = certificate;
    }

    /// <summary>The root certificate (public part only).</summary>
    public X509Certificate2 Certificate { get; }

    /// <summary>The root's common name.</summary>
    public string Name => Certificate.GetNameInfo(X509NameType.SimpleName, false);

    /// <summary>The root certificate as PEM, for the trust store.</summary>
    public string CertificatePem => PemEncoding.WriteString("CERTIFICATE", Certificate.RawData);

    /// <summary>The root's public key as an uncompressed point (04 || X || Y).</summary>
    public byte[] PublicKeyPoint
    {
        get
        {
            var q = _key.ExportParameters(false).Q;
            return [0x04, .. q.X!, .. q.Y!];
        }
    }

    /// <summary>Creates a new root: O=<see cref="Organization"/>, CN=<paramref name="commonName"/>, valid 25 years.</summary>
    public static LeafTestCa Create(string commonName = DefaultName)
    {
        if (string.IsNullOrWhiteSpace(commonName))
            throw new ArgumentException("The test root needs a name.", nameof(commonName));

        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        // The builder encodes in reverse order of addition: this encodes O then CN, as leaf-tool does
        var subject = new X500DistinguishedNameBuilder();
        subject.AddCommonName(commonName.Trim());
        subject.AddOrganizationName(Organization);

        var request = new CertificateRequest(subject.Build(), key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var now = DateTimeOffset.UtcNow;
        using var withKey = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(25));
        return new LeafTestCa(key, X509CertificateLoader.LoadCertificate(withKey.RawData));
    }

    /// <summary>
    /// Loads <c>ca.key</c> and <c>ca.crt</c> from <paramref name="directory"/>.
    /// </summary>
    /// <exception cref="IOException">The files are missing or unreadable.</exception>
    /// <exception cref="ArgumentException">The files are not a matching P-256 key and certificate.</exception>
    public static LeafTestCa Load(string directory)
    {
        var keyPath = Path.Combine(directory, KeyFileName);
        var certificatePath = Path.Combine(directory, CertificateFileName);
        if (!File.Exists(keyPath) || !File.Exists(certificatePath))
            throw new FileNotFoundException(
                $"A test root needs both {KeyFileName} and {CertificateFileName} in {directory}.");

        var key = ECDsa.Create();
        try
        {
            key.ImportFromPem(File.ReadAllText(keyPath));
            var certificate = LoadCertificate(File.ReadAllBytes(certificatePath));

            using var certificateKey = certificate.GetECDsaPublicKey()
                ?? throw new ArgumentException($"{CertificateFileName} does not hold an EC key.");
            var parameters = key.ExportParameters(false);
            if (parameters.Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
                throw new ArgumentException($"{KeyFileName} is not a P-256 key.");
            if (!certificateKey.ExportParameters(false).Q.X!.AsSpan().SequenceEqual(parameters.Q.X)
                || !certificateKey.ExportParameters(false).Q.Y!.AsSpan().SequenceEqual(parameters.Q.Y))
                throw new ArgumentException($"{KeyFileName} does not match {CertificateFileName}.");

            return new LeafTestCa(key, certificate);
        }
        catch (Exception ex) when (ex is CryptographicException)
        {
            key.Dispose();
            throw new ArgumentException($"The test root in {directory} could not be read: {ex.Message}", ex);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Writes <c>ca.key</c> and <c>ca.crt</c> into <paramref name="directory"/>, creating it if needed.
    /// Refuses to overwrite an existing test root.
    /// </summary>
    public void Save(string directory)
    {
        var keyPath = Path.Combine(directory, KeyFileName);
        var certificatePath = Path.Combine(directory, CertificateFileName);
        if (File.Exists(keyPath) || File.Exists(certificatePath))
            throw new IOException($"{directory} already holds a test root; load it instead of overwriting it.");

        Directory.CreateDirectory(directory);
        File.WriteAllText(keyPath, _key.ExportPkcs8PrivateKeyPem() + "\n");
        File.WriteAllText(certificatePath, CertificatePem + "\n");
    }

    /// <summary>
    /// Issues a card certificate binding <paramref name="cardPublicKey"/> to <paramref name="openId"/>.
    /// </summary>
    /// <param name="cardPublicKey">The card's uncompressed P-256 point (65 bytes).</param>
    /// <param name="openId">0 to 2^38 - 1.</param>
    /// <returns>The DER certificate.</returns>
    public byte[] IssueCardCertificate(byte[] cardPublicKey, long openId)
    {
        if (openId is < 0 or > Models.TechnologyDetails.LEAFVerifiedDetails.OpenIdMax)
            throw new ArgumentOutOfRangeException(nameof(openId),
                $"Open ID must be 0 to {Models.TechnologyDetails.LEAFVerifiedDetails.OpenIdMax}.");
        if (cardPublicKey is not { Length: 65 } || cardPublicKey[0] != 0x04)
            throw new ArgumentException("The card public key is not an uncompressed P-256 point.", nameof(cardPublicKey));

        using var cardKey = ImportCardKey(cardPublicKey);

        var subject = new X500DistinguishedNameBuilder();
        subject.Add(SerialNumberOid, openId.ToString("D12"), UniversalTagNumber.PrintableString);

        var request = new CertificateRequest(subject.Build(), cardKey, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(
            X509AuthorityKeyIdentifierExtension.CreateFromCertificate(Certificate, true, false));

        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] = (byte)((serial[0] & 0x7F) | 0x40); // positive, full length

        // The generator overload does not cap notAfter at the issuer's, so cards never expire
        using var certificate = request.Create(
            Certificate.SubjectName,
            X509SignatureGenerator.CreateForECDsa(_key),
            DateTimeOffset.UtcNow.AddDays(-1),
            NoExpiry,
            serial);
        return certificate.RawData;
    }

    public void Dispose()
    {
        _key.Dispose();
        Certificate.Dispose();
    }

    private static ECDsa ImportCardKey(byte[] point)
    {
        try
        {
            return ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = point[1..33], Y = point[33..] }
            });
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException)
        {
            // Windows reports an off-curve point as an unsupported curve
            throw new ArgumentException("The card public key is not a point on P-256.", nameof(point), ex);
        }
    }

    private static X509Certificate2 LoadCertificate(byte[] file)
    {
        var text = System.Text.Encoding.ASCII.GetString(file);
        return text.Contains("-----BEGIN CERTIFICATE-----")
            ? X509Certificate2.CreateFromPem(text)
            : X509CertificateLoader.LoadCertificate(file);
    }
}
