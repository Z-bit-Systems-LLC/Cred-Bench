using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace CredBench.Core.Services.Leaf;

/// <summary>
/// A root key that LEAF Verified card certificates may be signed by.
/// </summary>
/// <param name="Name">Display name, e.g. "NXP LEAF RootCAvE200" or the test root's CN.</param>
/// <param name="PublicKey">Uncompressed P-256 point: 04 || X || Y (65 bytes).</param>
/// <param name="IsBuiltIn">True for the production anchor shipped with Cred-Bench.</param>
/// <param name="CertificatePem">The root certificate as PEM, for test roots loaded from a file.</param>
public sealed record LeafTrustAnchor(string Name, byte[] PublicKey, bool IsBuiltIn, string? CertificatePem = null)
{
    public string PublicKeyHex => Convert.ToHexString(PublicKey);

    public string Kind => IsBuiltIn ? "Built-in" : "Test root";

    /// <summary>Creates an ECDsa verifier for this anchor. The caller disposes it.</summary>
    public ECDsa CreateVerifier() => ECDsa.Create(new ECParameters
    {
        Curve = ECCurve.NamedCurves.nistP256,
        Q = new ECPoint { X = PublicKey[1..33], Y = PublicKey[33..65] }
    });

    public bool Equals(LeafTrustAnchor? other) =>
        other is not null && PublicKey.AsSpan().SequenceEqual(other.PublicKey);

    public override int GetHashCode() => Convert.ToHexString(PublicKey).GetHashCode();
}

/// <summary>
/// The trust anchors a LEAF Verified reader checks card certificates against.
/// </summary>
public interface ILeafTrustStore
{
    /// <summary>The built-in production anchor first, then any test roots, in the order added.</summary>
    IReadOnlyList<LeafTrustAnchor> Anchors { get; }
}

/// <summary>
/// Trust store holding the built-in NXP LEAF RootCAvE200 anchor plus any test roots the user adds.
/// Thread-safe: <see cref="Anchors"/> is an immutable snapshot.
/// </summary>
public class LeafTrustStore : ILeafTrustStore
{
    private const string P256Oid = "1.2.840.10045.3.1.7";

    /// <summary>NXP LEAF RootCAvE200 (P-256), as published in the LEAF Verified onboarding guide.</summary>
    public static readonly LeafTrustAnchor NxpRootCaE200 = new(
        "NXP LEAF RootCAvE200",
        Convert.FromHexString(
            "042D2781BE41C22758A613810F67EC78DF1176C4765B212B49218C6C58408A5ADA" +
            "3D9973209D8228913A8816973CFE5C9EBFD8C6697532CDD5B53EE134D2F11B3C"),
        IsBuiltIn: true);

    private readonly Lock _lock = new();
    private IReadOnlyList<LeafTrustAnchor> _anchors = [NxpRootCaE200];

    /// <summary>Raised after a test root is added or removed.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<LeafTrustAnchor> Anchors => _anchors;

    /// <summary>The test roots only, in the order added.</summary>
    public IReadOnlyList<LeafTrustAnchor> TestRoots => _anchors.Where(a => !a.IsBuiltIn).ToList();

    /// <summary>
    /// Adds a test root from a certificate file (PEM or DER), such as the <c>ca.crt</c>
    /// written by Cred-Bench or AsymCred's <c>leaf-tool init-ca</c>.
    /// Returns the existing anchor if one with the same key is already trusted.
    /// </summary>
    /// <exception cref="ArgumentException">The file is not a P-256 certificate.</exception>
    public LeafTrustAnchor AddTestRoot(byte[] certificateFile)
    {
        var anchor = ParseRootCertificate(certificateFile);

        lock (_lock)
        {
            var existing = _anchors.FirstOrDefault(a => a.Equals(anchor));
            if (existing is not null)
                return existing;

            _anchors = [.. _anchors, anchor];
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return anchor;
    }

    /// <summary>Removes a test root. The built-in anchor cannot be removed.</summary>
    public bool RemoveTestRoot(LeafTrustAnchor anchor)
    {
        if (anchor.IsBuiltIn)
            return false;

        lock (_lock)
        {
            if (!_anchors.Contains(anchor))
                return false;

            _anchors = _anchors.Where(a => !a.Equals(anchor)).ToList();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Parses a P-256 root certificate (PEM or DER) into a test-root anchor named after its CN.
    /// </summary>
    /// <exception cref="ArgumentException">The file is not a P-256 certificate.</exception>
    public static LeafTrustAnchor ParseRootCertificate(byte[] certificateFile)
    {
        X509Certificate2 cert;
        try
        {
            var text = Encoding.ASCII.GetString(certificateFile);
            cert = text.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal)
                ? X509Certificate2.CreateFromPem(text)
                : X509CertificateLoader.LoadCertificate(certificateFile);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            throw new ArgumentException($"Not a certificate: {ex.Message}", nameof(certificateFile), ex);
        }

        using (cert)
        {
            using var key = cert.GetECDsaPublicKey()
                ?? throw new ArgumentException("The certificate does not hold an EC public key.", nameof(certificateFile));

            var parameters = key.ExportParameters(false);
            if (parameters.Curve.Oid?.Value != P256Oid)
                throw new ArgumentException("The certificate's key is not P-256.", nameof(certificateFile));

            byte[] point = [0x04, .. parameters.Q.X!, .. parameters.Q.Y!];

            var name = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            if (string.IsNullOrWhiteSpace(name))
                name = cert.Subject;

            return new LeafTrustAnchor(name, point, IsBuiltIn: false, cert.ExportCertificatePem());
        }
    }
}
