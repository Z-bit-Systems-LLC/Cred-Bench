using System.Windows;
using ZBitSystems.Wpf.UI.Helpers;

namespace CredBench.Windows.Views;

/// <summary>
/// About dialog: application name, version, copyright and the licenses Cred Bench ships under.
/// </summary>
public partial class AboutWindow
{
    public AboutWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    // Read from this assembly rather than the entry assembly, which is the test host under the UI tests
    public string AppVersion { get; } = ApplicationInfoHelper.GetVersion(typeof(AboutWindow).Assembly);

    public string Copyright { get; } = ApplicationInfoHelper.GetCopyright(typeof(AboutWindow).Assembly);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
