namespace CredBench.Core.Services.GlobalPlatform;

/// <summary>
/// Represents a single entry returned by the GlobalPlatform GET STATUS command.
/// </summary>
public record CardContentEntry
{
    public required byte[] Aid { get; init; }
    public required byte LifecycleState { get; init; }
    public required byte Privileges { get; init; }

    /// <summary>AID formatted as hex string (e.g. "A0000001510000").</summary>
    public string AidHex => BitConverter.ToString(Aid).Replace("-", "");

    /// <summary>Human-readable lifecycle state.</summary>
    public string LifecycleDescription => LifecycleState switch
    {
        0x01 => "Loaded",
        0x03 => "Installed",
        0x07 => "Selectable",
        0x0F => "Personalized",
        0x83 => "Locked",
        0xFF => "Terminated",
        _ => $"0x{LifecycleState:X2}"
    };
}

/// <summary>
/// The type of card content queried via GET STATUS.
/// </summary>
public enum CardContentType : byte
{
    /// <summary>Issuer Security Domain and supplementary security domains.</summary>
    IssuerSecurityDomain = 0x80,

    /// <summary>Executable Load Files (packages).</summary>
    ExecutableLoadFiles = 0x20,

    /// <summary>Executable Load Files and their Executable Modules.</summary>
    ExecutableLoadFilesAndModules = 0x10,

    /// <summary>Applications and Security Domains.</summary>
    Applications = 0x40,
}

/// <summary>
/// Combined card content from multiple GET STATUS queries.
/// </summary>
public record CardContent
{
    public required IReadOnlyList<CardContentEntry> Packages { get; init; }
    public required IReadOnlyList<CardContentEntry> Applets { get; init; }

    /// <summary>Free persistent (NVM/EEPROM) memory in bytes, if available.</summary>
    public int? FreePersistentMemory { get; init; }

    /// <summary>Free transient reset memory in bytes, if available.</summary>
    public int? FreeTransientResetMemory { get; init; }

    /// <summary>Free transient deselect memory in bytes, if available.</summary>
    public int? FreeTransientDeselectMemory { get; init; }

    /// <summary>Whether the PKOC applet (A000000898000001) is installed.</summary>
    public bool HasPkocApplet => Applets.Any(
        a => a.AidHex.Equals("A000000898000001", StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether the PKOC package (A000000898000002) is loaded.</summary>
    public bool HasPkocPackage => Packages.Any(
        p => p.AidHex.Equals("A000000898000002", StringComparison.OrdinalIgnoreCase));
}
