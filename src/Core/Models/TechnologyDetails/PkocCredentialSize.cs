namespace CredBench.Core.Models.TechnologyDetails;

/// <summary>
/// How many bits of the public key's X component a PKOC credential uses (PSIA PKOC spec).
/// </summary>
public enum PkocCredentialSize
{
    /// <summary>The full X component.</summary>
    Bits256,

    /// <summary>The lower 75 bits of X, recommended for legacy panels.</summary>
    Bits75,

    /// <summary>The lower 64 bits of X, the minimum for legacy panels.</summary>
    Bits64
}
