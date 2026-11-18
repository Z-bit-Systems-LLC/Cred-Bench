namespace CredBench.Core.Models.TechnologyDetails;

/// <summary>
/// How a credential number is written out for display.
/// </summary>
public enum CredentialFormat
{
    /// <summary>A binary bit string, as sent over Wiegand.</summary>
    WiegandBits,

    /// <summary>Hexadecimal, most significant byte first.</summary>
    Hex,

    /// <summary>The unsigned decimal value.</summary>
    Decimal
}
