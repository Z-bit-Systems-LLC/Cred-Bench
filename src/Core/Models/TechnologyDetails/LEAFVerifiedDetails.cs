namespace CredBench.Core.Models.TechnologyDetails;

/// <summary>
/// How far a LEAF Verified certificate could be trusted.
/// </summary>
public enum LeafTrust
{
    /// <summary>The certificate meets the LEAF profile and verifies under a trust anchor.</summary>
    Verified,

    /// <summary>The certificate meets the LEAF profile but no trust anchor signed it.</summary>
    UntrustedIssuer,

    /// <summary>The certificate parsed but breaks one or more LEAF profile rules.</summary>
    ProfileViolation,

    /// <summary>The certificate could not be read from the card or could not be parsed.</summary>
    Unreadable
}

/// <summary>
/// LEAF Verified (asymmetric LEAF on MIFARE DUOX) details: the Open ID from the card certificate,
/// its W40-2 Wiegand encoding, and the result of both signature checks.
/// </summary>
public record LEAFVerifiedDetails
{
    /// <summary>Largest Open ID that fits the 38 data bits of a W40-2 frame (2^38 - 1).</summary>
    public const long OpenIdMax = (1L << 38) - 1;

    /// <summary>The Open ID: the card certificate's subject serialNumber.</summary>
    public long? OpenId { get; init; }

    /// <summary>The card's P-256 public key from the certificate, 04||X||Y as hex.</summary>
    public string? CardPublicKeyHex { get; init; }

    /// <summary>The certificate issuer's distinguished name.</summary>
    public string? Issuer { get; init; }

    /// <summary>The certificate's subject distinguished name.</summary>
    public string? Subject { get; init; }

    /// <summary>The certificate's Authority Key Identifier as hex, if present.</summary>
    public string? AuthorityKeyId { get; init; }

    /// <summary>The certificate's serial number as hex.</summary>
    public string? CertificateSerial { get; init; }

    /// <summary>The certificate as read from file 2 (DER), kept even when it does not parse.</summary>
    public byte[]? Certificate { get; init; }

    public LeafTrust Trust { get; init; } = LeafTrust.Unreadable;

    /// <summary>The name of the trust anchor that signed the certificate, or null if none did.</summary>
    public string? TrustAnchorName { get; init; }

    /// <summary>
    /// Result of the ISOInternalAuthenticate challenge, verified with the certificate's key.
    /// Null when the challenge was not sent or the card's answer could not be checked.
    /// </summary>
    public bool? CardSignatureValid { get; init; }

    /// <summary>Every LEAF certificate profile rule the certificate breaks.</summary>
    public IReadOnlyList<string> ProfileIssues { get; init; } = [];

    /// <summary>A description of the step that failed, if the read did not complete.</summary>
    public string? Error { get; init; }

    /// <summary>The status word of the last failed command, as hex (e.g. "91BE").</summary>
    public string? LastStatusWord { get; init; }

    /// <summary>
    /// The one-time tap URL from the card's NDEF application (F51CD5), if it has one.
    /// It changes on every read (Secure Unique NFC) and only the issuer's server can check it.
    /// </summary>
    public string? TapUrl { get; init; }

    /// <summary>The number printed on the card, from an opt-in lookup of <see cref="TapUrl"/>.</summary>
    public string? PrintedId { get; init; }

    /// <summary>Where the Printed ID came from and how far it was verified, or why the lookup failed.</summary>
    public string? PrintedIdNote { get; init; }

    /// <summary>True only when the certificate chains to a trust anchor and the card proved it holds the key.</summary>
    public bool IsFullyVerified => Trust == LeafTrust.Verified && CardSignatureValid == true;

    /// <summary>W40-2 frame as 10 hex characters.</summary>
    public string? W40Hex => OpenId is { } id and >= 0 and <= OpenIdMax ? ToW40(id).ToString("X10") : null;

    /// <summary>W40-2 frame as 40 '0'/'1' characters, first transmitted bit first.</summary>
    public string? W40Bits => OpenId is { } id and >= 0 and <= OpenIdMax
        ? Convert.ToString(ToW40(id), 2).PadLeft(40, '0')
        : null;

    /// <summary>
    /// Encodes an Open ID as a W40-2 frame: bit 0 is even parity over bits 1-19, bits 1-38
    /// are the Open ID MSB first, bit 39 is odd parity over bits 0-38.
    /// Returned as a 40-bit integer whose most significant bit is the first transmitted bit.
    /// </summary>
    public static long ToW40(long openId)
    {
        if (openId is < 0 or > OpenIdMax)
            throw new ArgumentOutOfRangeException(nameof(openId), "Open ID does not fit 38 bits");

        var even = (long)(System.Numerics.BitOperations.PopCount((ulong)(openId >> 19)) & 1);
        var odd = ((even + System.Numerics.BitOperations.PopCount((ulong)openId)) & 1) ^ 1;
        return (even << 39) | (openId << 1) | odd;
    }

    /// <summary>
    /// Returns non-null fields as labeled display pairs for text output.
    /// </summary>
    public IReadOnlyList<(string Label, string Value)> GetFields()
    {
        List<(string, string)> fields = [("Status", DescribeStatus())];
        if (OpenId is { } openId) fields.Add(("Open ID", openId.ToString()));
        if (W40Hex is { } w40Hex) fields.Add(("W40-2 Hex", w40Hex));
        if (W40Bits is { } w40Bits) fields.Add(("W40-2 Wiegand", w40Bits));
        if (PrintedId is { } printedId) fields.Add(("Printed ID", printedId));
        if (PrintedIdNote is { } printedIdNote) fields.Add(("Printed ID Lookup", printedIdNote));
        fields.Add(("Certificate", DescribeTrust()));
        if (TrustAnchorName is { } anchor) fields.Add(("Trust Anchor", anchor));
        if (Issuer is { } issuer) fields.Add(("Issuer", issuer));
        if (AuthorityKeyId is { } aki) fields.Add(("Authority Key ID", aki));
        fields.Add(("Card Signature", CardSignatureValid switch
        {
            true => "Valid",
            false => "INVALID",
            null => "Not checked"
        }));
        if (CardPublicKeyHex is { } key) fields.Add(("Card Public Key", key));
        if (TapUrl is { } tapUrl) fields.Add(("Tap URL", tapUrl));
        foreach (var issue in ProfileIssues) fields.Add(("Profile Issue", issue));
        if (Error is { } error) fields.Add(("Error", error));
        if (LastStatusWord is { } sw) fields.Add(("Status Word", sw));
        return fields;
    }

    private string DescribeStatus()
    {
        if (IsFullyVerified)
            return "Verified";
        if (CardSignatureValid == false)
            return "Card failed the challenge (possible clone)";
        return Trust switch
        {
            LeafTrust.UntrustedIssuer => "Unverified: certificate not signed by a trusted root",
            LeafTrust.ProfileViolation => "Unverified: certificate breaks the LEAF profile",
            LeafTrust.Unreadable => "Unverified: certificate could not be read",
            _ => "Unverified: card challenge not completed"
        };
    }

    private string DescribeTrust() => Trust switch
    {
        LeafTrust.Verified => "Trusted",
        LeafTrust.UntrustedIssuer => "Untrusted issuer",
        LeafTrust.ProfileViolation => TrustAnchorName is null ? "Profile violation" : "Profile violation (signature trusted)",
        _ => "Unreadable"
    };
}
