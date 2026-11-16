using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using CredBench.Core.Models;
using CredBench.Core.Services;
using CredBench.Core.Services.CardDetectors;
using CredBench.Windows;
using CredBench.Windows.Services;
using CredBench.Windows.Settings;
using CredBench.Windows.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;
using ZBitSystems.Wpf.UI.Settings;

namespace CredBench.UI.Tests;

/// <summary>
/// Test application that replaces the PC/SC services and card detectors with mocks.
/// Does not call InitializeComponent to avoid XAML resource loading issues
/// across assemblies — instead loads resource dictionaries manually.
/// </summary>
public class TestApp : App
{
    /// <summary>
    /// The readers the smart card service reports unless a test says otherwise.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultReaders = ["Test Reader 1", "Test Reader 2"];

    public const string TestAtr = "3B8F8001804F0CA0000003060300030000000068";
    public const string TestUid = "04A1B2C3D4E5F6";

    /// <summary>
    /// The settings folder the test app reads and writes in place of the user's own, so window
    /// state saved by one run cannot change the layout the next run starts from.
    /// </summary>
    private const string SettingsAppName = "CredBench.UITests";

    /// <summary>
    /// The technologies the app detects, in the order the app runs its detectors.
    /// </summary>
    private static readonly CardTechnology[] DetectorOrder =
    [
        CardTechnology.LEAFVerified,
        CardTechnology.PIV,
        CardTechnology.DESFire,
        CardTechnology.ISO14443,
        CardTechnology.PKOC,
        CardTechnology.LEAF
    ];

    private IHost? _testHost;

    public Mock<ISmartCardService> MockSmartCard { get; private set; } = new();
    public Mock<IReaderMonitorService> MockReaderMonitor { get; private set; } = new();
    public Mock<ICardConnection> MockConnection { get; private set; } = new();

    /// <summary>
    /// One scripted detector per technology; each reports its technology absent until a test
    /// tells it otherwise.
    /// </summary>
    public IReadOnlyDictionary<CardTechnology, ScriptedCardDetector> Detectors { get; private set; } =
        CreateDetectors();

    public TestApp()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        ConfigureMockDefaults();
    }

    /// <summary>
    /// Gets a registered service from the test host.
    /// </summary>
    public T GetService<T>() where T : class
    {
        return _testHost!.Services.GetRequiredService<T>();
    }

    private static Dictionary<CardTechnology, ScriptedCardDetector> CreateDetectors() =>
        DetectorOrder.ToDictionary(technology => technology, technology => new ScriptedCardDetector(technology));

    /// <summary>
    /// Replaces every mock and detector with a fresh one, so nothing a previous fixture set up or
    /// subscribed carries over into the next host.
    /// </summary>
    private void ReplaceMocks()
    {
        MockSmartCard = new Mock<ISmartCardService>();
        MockReaderMonitor = new Mock<IReaderMonitorService>();
        MockConnection = new Mock<ICardConnection>();
        Detectors = CreateDetectors();

        ConfigureMockDefaults();
    }

    /// <summary>
    /// Closes every window and discards the host behind them, then starts a new host with fresh
    /// mocks and a new main window. Every singleton view model is rebuilt along with it.
    /// </summary>
    public async Task RestartTestApp()
    {
        foreach (var window in Windows.OfType<Window>().ToList())
        {
            window.Close();
        }

        await StopTestApp();
        ReplaceMocks();
        StartTestApp();
    }

    private void ConfigureMockDefaults()
    {
        MockSmartCard.Setup(s => s.GetReaders()).Returns(DefaultReaders);
        MockSmartCard.Setup(s => s.Connect(It.IsAny<string>())).Returns(() => MockConnection.Object);

        MockConnection.Setup(c => c.ReaderName).Returns(DefaultReaders[0]);
        MockConnection.Setup(c => c.GetATR()).Returns(TestAtr);
        MockConnection.Setup(c => c.GetUID()).Returns(TestUid);
        MockConnection.Setup(c => c.GetProtocol()).Returns("T1");

        // Anything the dialogs send straight to the card is answered "file or application not found"
        MockConnection.Setup(c => c.Transmit(It.IsAny<byte[]>())).Returns([0x6A, 0x82]);
    }

    /// <summary>
    /// Loads the application resource dictionaries that would normally be
    /// loaded by InitializeComponent via App.xaml.
    /// </summary>
    private void LoadResources()
    {
        Resources = new ResourceDictionary();
        Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
        Resources.MergedDictionaries.Add(new ControlsDictionary());
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/ZBitSystems.Wpf.UI;component/Styles/DesignTokens.xaml")
        });
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/ZBitSystems.Wpf.UI;component/Styles/ThemeSemanticColors.xaml")
        });
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/ZBitSystems.Wpf.UI;component/Styles/ComponentStyles.xaml")
        });
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/CredBench;component/Styles/LayoutTemplates.xaml")
        });

        // Declared inline in App.xaml
        var standard = TryFindResource("TextBox.Standard") as Style
            ?? throw new InvalidOperationException("The Guidelines styles have no TextBox.Standard.");
        var copyable = new Style(typeof(TextBox), standard);
        copyable.Setters.Add(new Setter(TextBoxBase.IsReadOnlyProperty, true));
        copyable.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Consolas")));
        Resources["TextBox.Copyable"] = copyable;
    }

    /// <summary>
    /// Starts the test application with mocked services and shows the main window.
    /// </summary>
    public void StartTestApp()
    {
        // The view model posts card events through the context it is created on, as it does in the app
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher));

        ZBitSystems.Wpf.UI.Localization.LocalizationService.Provider =
            new ResourceLocalizationProvider();

        LoadResources();
        ClearSettings();

        _testHost = BuildHost(services =>
        {
            // Replace the PC/SC services with mocks
            services.RemoveAll<ISmartCardService>();
            services.AddSingleton(MockSmartCard.Object);
            services.RemoveAll<IReaderMonitorService>();
            services.AddSingleton(MockReaderMonitor.Object);

            // Replace the detectors with scripted ones, in the order the app runs its own
            services.RemoveAll<ICardDetector>();
            foreach (var technology in DetectorOrder)
            {
                services.AddSingleton<ICardDetector>(Detectors[technology]);
            }

            // The main window restores and saves its size and position through the settings service
            services.RemoveAll<JsonUserSettingsService<CredBenchSettings>>();
            services.AddSingleton(new JsonUserSettingsService<CredBenchSettings>(SettingsAppName));
        });

        _testHost.Start();
        _testHost.Services.GetRequiredService<IReaderMonitorService>().Start();

        var mainWindow = _testHost.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    /// <summary>
    /// Deletes the test settings, so every host starts from the default window size and position
    /// whatever an earlier run saved.
    /// </summary>
    private static void ClearSettings()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), SettingsAppName);

        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
        catch (IOException)
        {
            // A settings file left from an earlier run only changes where the window opens
        }
    }

    /// <summary>
    /// Shuts down the test application.
    /// </summary>
    public async Task StopTestApp()
    {
        if (_testHost != null)
        {
            await _testHost.StopAsync();
            _testHost.Dispose();
            _testHost = null;
        }
    }
}
