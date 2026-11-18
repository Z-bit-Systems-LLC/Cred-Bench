using ZBitSystems.Wpf.UI.Settings;

namespace CredBench.Windows.Settings;

/// <summary>
/// Cred-Bench user settings: window state from <see cref="UserSettings"/> plus app-specific values.
/// </summary>
public class CredBenchSettings : UserSettings
{
    /// <summary>
    /// Starts a fresh install wider than the Guidelines default (800×600), which squeezes the card
    /// panel and the detail tabs side by side. <c>WindowStateManager</c> shrinks it to fit smaller screens.
    /// </summary>
    public CredBenchSettings()
    {
        WindowWidth = 1200;
        WindowHeight = 800;
    }

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
