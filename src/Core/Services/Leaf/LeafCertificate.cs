using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace CredBench.Core.Services.Leaf;

/// <summary>
/// A LEAF Verified card certificate, checked against the profile AsymCred enforces
/// (leaf/src/leaf_cert.c, LeafCertificate.java):
/// <list type="bullet">
///   <item>ecdsa-with-SHA256, identical inside and outside tbsCertificate;</item>
///   <item>SubjectPublicKeyInfo id-ecPublicKey + prime256v1, uncompressed point;</item>
///   <item>exactly one subject serialNumber of 1-12 decimal digits, at most 2^38 - 1 (the Open ID).</item>
/// </list>
/// Unlike AsymCred, which refuses at the first violation, every violation is collected in
/// <see cref="ProfileIssues"/> so a bench can report all of them. Validity dates are not checked:
/// readers have no trusted clock.
/// </summary>
public sealed partial class LeafCertificate
{
    // AlgorithmIdentifier { ecdsa-with-SHA256 } with no parameters
    private static readonly byte[] AlgEcdsaSha256 = Convert.FromHexString("300A06082A8648CE3D040302");

    // AlgorithmIdentifier { id-ecPublicKey, prime256v1 }
    private static readonly byte[] AlgEcP256 = Convert.FromHexString("301306072A8648CE3D020106082A8648CE3D030107");

    private const string SerialNumberOid = "2.5.4.5";

    private readonly ReadOnlyMemory<byte> _tbs;
    private readonly byte[] _signature;

    private LeafCertificate(
        byte[] encoded, ReadOnlyMemory<byte> tbs, byte[] signature, byte[]? publicKey,
        long? openId, string issuer, string subject, string serialHex, string? authorityKeyId,
        IReadOnlyList<string> profileIssues)
    {
        Encoded = encoded;
        _tbs = tbs;
        _signature = signature;
        PublicKey = publicKey;
        OpenId = openId;
        Issuer = issuer;
        Subject = subject;
        SerialHex = serialHex;
        AuthorityKeyId = authorityKeyId;
        ProfileIssues = profileIssues;
    }

    /// <summary>The certificate exactly as given (DER).</summary>
    public byte[] Encoded { get; }

    /// <summary>The card's key, 04 || X || Y, or null if the key is not an uncompressed P-256 point.</summary>
    public byte[]? PublicKey { get; }

    /// <summary>The Open ID, or null if the subject serialNumber is missing, repeated or invalid.</summary>
    public long? OpenId { get; }

    public string Issuer { get; }

    public string Subject { get; }

    public string SerialHex { get; }

    public string? AuthorityKeyId { get; }

    /// <summary>Every profile rule the certificate breaks; empty if it meets the LEAF profile.</summary>
    public IReadOnlyList<string> ProfileIssues { get; }

    /// <summary>True if <paramref name="anchor"/> signed this certificate's tbsCertificate.</summary>
    public bool IsSignedBy(LeafTrustAnchor anchor)
    {
        try
        {
            using var key = anchor.CreateVerifier();
            return key.VerifyData(_tbs.Span, _signature, HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// Parses a certificate and checks it against the LEAF profile.
    /// </summary>
    /// <exception cref="FormatException">The bytes are not an X.509 certificate.</exception>
    public static LeafCertificate Parse(byte[] der)
    {
        try
        {
            return ParseOrThrow(der);
        }
        catch (AsnContentException ex)
        {
            throw new FormatException($"Not a DER certificate: {ex.Message}", ex);
        }
    }

    private static LeafCertificate ParseOrThrow(byte[] der)
    {
        List<string> issues = [];

        var outer = new AsnReader(der, AsnEncodingRules.DER);
        var cert = outer.ReadSequence();
        if (outer.HasData)
            issues.Add("Trailing bytes after the certificate");

        var tbsEncoded = cert.ReadEncodedValue();
        var outerAlg = cert.ReadEncodedValue();
        var signatureValue = cert.ReadBitString(out var unusedBits);
        if (unusedBits != 0)
            throw new FormatException("Malformed signatureValue");

        if (!outerAlg.Span.SequenceEqual(AlgEcdsaSha256))
            issues.Add("Signature algorithm is not ecdsa-with-SHA256");

        var tbs = new AsnReader(tbsEncoded, AsnEncodingRules.DER).ReadSequence();
        if (tbs.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
            tbs.ReadEncodedValue(); // [0] version

        var serial = tbs.ReadIntegerBytes();
        var innerAlg = tbs.ReadEncodedValue();
        var issuer = tbs.ReadEncodedValue();
        tbs.ReadEncodedValue(); // validity: not checked
        var subject = tbs.ReadEncodedValue();
        var spki = tbs.ReadSequence();

        if (!innerAlg.Span.SequenceEqual(AlgEcdsaSha256))
            issues.Add("Inner signature algorithm is not ecdsa-with-SHA256");
        else if (!innerAlg.Span.SequenceEqual(outerAlg.Span))
            issues.Add("Inner and outer signature algorithms differ");

        var keyAlg = spki.ReadEncodedValue();
        var keyBits = spki.ReadBitString(out _);
        byte[]? publicKey = null;
        if (!keyAlg.Span.SequenceEqual(AlgEcP256))
            issues.Add("Public key is not P-256 (id-ecPublicKey, prime256v1)");
        else if (keyBits.Length != 65 || keyBits[0] != 0x04)
            issues.Add("Public key is not an uncompressed point");
        else
            publicKey = keyBits;

        var openId = ReadOpenId(subject, issues);

        return new LeafCertificate(
            der.ToArray(),
            tbsEncoded,
            signatureValue,
            publicKey,
            openId,
            new X500DistinguishedName(issuer.Span).Name,
            new X500DistinguishedName(subject.Span).Name,
            Convert.ToHexString(serial.Span),
            ReadAuthorityKeyId(der),
            issues);
    }

    private static long? ReadOpenId(ReadOnlyMemory<byte> name, List<string> issues)
    {
        List<string> values = [];

        var rdns = new AsnReader(name, AsnEncodingRules.DER).ReadSequence();
        while (rdns.HasData)
        {
            var rdn = rdns.ReadSetOf(skipSortOrderValidation: true);
            while (rdn.HasData)
            {
                var atv = rdn.ReadSequence();
                if (atv.ReadObjectIdentifier() != SerialNumberOid)
                    continue;

                var tag = atv.PeekTag();
                if (tag.HasSameClassAndValue(new Asn1Tag(UniversalTagNumber.PrintableString)))
                {
                    values.Add(atv.ReadCharacterString(UniversalTagNumber.PrintableString));
                }
                else if (tag.HasSameClassAndValue(new Asn1Tag(UniversalTagNumber.UTF8String)))
                {
                    values.Add(atv.ReadCharacterString(UniversalTagNumber.UTF8String));
                }
                else
                {
                    issues.Add("Subject serialNumber is not a PrintableString or UTF8String");
                    values.Add("");
                }
            }
        }

        switch (values.Count)
        {
            case 0:
                issues.Add("No subject serialNumber (Open ID)");
                return null;
            case > 1:
                issues.Add("More than one subject serialNumber");
                return null;
        }

        var digits = values[0];
        if (!OpenIdPattern().IsMatch(digits))
        {
            if (digits.Length > 0)
                issues.Add($"Open ID is not 1-12 decimal digits: \"{digits}\"");
            return null;
        }

        var id = long.Parse(digits);
        if (id > Models.TechnologyDetails.LEAFVerifiedDetails.OpenIdMax)
        {
            issues.Add($"Open ID does not fit 38 bits: {digits}");
            return null;
        }

        return id;
    }

    private static string? ReadAuthorityKeyId(byte[] der)
    {
        try
        {
            using var x509 = X509CertificateLoader.LoadCertificate(der);
            var aki = x509.Extensions.OfType<X509AuthorityKeyIdentifierExtension>().FirstOrDefault();
            return aki?.KeyIdentifier is { } id ? Convert.ToHexString(id.Span) : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    [GeneratedRegex("^[0-9]{1,12}$")]
    private static partial Regex OpenIdPattern();
}
