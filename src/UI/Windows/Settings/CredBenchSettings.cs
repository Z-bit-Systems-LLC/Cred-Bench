using ZBitSystems.Wpf.UI.Settings;

namespace CredBench.Windows.Settings;

/// <summary>
/// Cred-Bench user settings: window state from <see cref="UserSettings"/> plus app-specific values.
/// </summary>
public class CredBenchSettings : UserSettings
{
    /// <summary>
    /// LEAF Verified test roots the user trusts, as PEM certificates. Stored as certificates rather
    /// than file paths so they survive the original files being moved or deleted.
    /// </summary>
    public List<string> LeafTestRoots { get; set; } = [];

    /// <summary>
    /// Folder of the LEAF test root (ca.key, ca.crt) last used for programming, reloaded at startup.
    /// </summary>
    public string? LeafTestCaDirectory { get; set; }
}
