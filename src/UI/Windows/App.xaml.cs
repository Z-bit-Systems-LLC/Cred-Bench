using System.Text;
using System.Windows;
using CredBench.Core.Services;
using CredBench.Core.Services.CardDetectors;
using CredBench.Core.Services.Leaf;
using CredBench.Core.Services.Pkoc;
using CredBench.Core.ViewModels;
using CredBench.Windows.Services;
using CredBench.Windows.Settings;
using CredBench.Windows.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ZBitSystems.Wpf.UI.Settings;

namespace CredBench.Windows;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        // Set up localization before DI so the provider is available for XAML
        var localizationProvider = new ResourceLocalizationProvider();
        ZBitSystems.Wpf.UI.Localization.LocalizationService.Provider = localizationProvider;

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                // Settings
                var settingsService = new JsonUserSettingsService<CredBenchSettings>("CredBench");
                services.AddSingleton<IUserSettingsService<CredBenchSettings>>(settingsService);
                services.AddSingleton<IUserSettingsService<UserSettings>>(settingsService);

                // LEAF Verified trust anchors: built-in NXP root plus persisted test roots
                services.AddSingleton(CreateLeafTrustStore(settingsService));
                services.AddSingleton<ILeafTrustStore>(sp => sp.GetRequiredService<LeafTrustStore>());

                // Localization
                services.AddSingleton<ILocalizationService, Core.Services.LocalizationService>();

                // Smart card services
                services.AddSingleton<WindowsSmartCardService>();
                services.AddSingleton<ISmartCardService>(sp => sp.GetRequiredService<WindowsSmartCardService>());
                services.AddSingleton<IReaderMonitorService, WindowsReaderMonitorService>();

                // Card detectors. LEAF Verified runs first: on a JavaCard emulation card the LEAF
                // applet is only reachable while it is still the default-selected applet, before
                // any ISO SELECT of another applet (e.g. PKOC) takes its place.
                services.AddSingleton<ICardDetector>(sp =>
                    new LEAFVerifiedDetector(sp.GetRequiredService<ILeafTrustStore>()));
                services.AddSingleton<ICardDetector, PIVDetector>();
                services.AddSingleton<ICardDetector, DESFireDetector>();
                services.AddSingleton<ICardDetector, ISO14443Detector>();
                services.AddSingleton<ICardDetector, PKOCDetector>();
                services.AddSingleton<ICardDetector, LEAFDetector>();

                // Detection service
                services.AddSingleton<CardDetectionService>();

                // LEAF Verified Printed ID lookup (opt-in, online)
                services.AddSingleton(_ => new LeafPrintedIdLookup());

                // PKOC and LEAF Verified programming
                services.AddSingleton<PkocCardProgrammer>();
                services.AddSingleton<LeafCardProgrammer>();

                // ViewModels
                services.AddSingleton<PkocViewModel>();
                services.AddSingleton<LeafVerifiedViewModel>();
                services.AddSingleton(sp => CreateProgramCardViewModel(sp, settingsService));
                services.AddSingleton<MainViewModel>();

                // Views
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    private static LeafTrustStore CreateLeafTrustStore(IUserSettingsService<CredBenchSettings> settingsService)
    {
        var store = new LeafTrustStore();
        foreach (var pem in settingsService.Settings.LeafTestRoots)
        {
            try
            {
                store.AddTestRoot(Encoding.ASCII.GetBytes(pem));
            }
            catch (ArgumentException)
            {
                // A corrupt entry is dropped; it is rewritten on the next change
            }
        }

        store.Changed += async (_, _) =>
        {
            settingsService.Settings.LeafTestRoots =
                [.. store.TestRoots.Select(a => a.CertificatePem).OfType<string>()];
            await settingsService.SaveAsync();
        };

        return store;
    }

    private static ProgramCardViewModel CreateProgramCardViewModel(
        IServiceProvider services, IUserSettingsService<CredBenchSettings> settingsService)
    {
        var viewModel = ActivatorUtilities.CreateInstance<ProgramCardViewModel>(services);
        viewModel.RestoreTestCa(settingsService.Settings.LeafTestCaDirectory);

        viewModel.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName != nameof(ProgramCardViewModel.TestCaDirectory)
                || settingsService.Settings.LeafTestCaDirectory == viewModel.TestCaDirectory)
                return;

            settingsService.Settings.LeafTestCaDirectory = viewModel.TestCaDirectory;
            await settingsService.SaveAsync();
        };

        return viewModel;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();

        // Start reader monitoring
        var monitorService = _host.Services.GetRequiredService<IReaderMonitorService>();
        monitorService.Start();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        // Stop reader monitoring
        var monitorService = _host.Services.GetService<IReaderMonitorService>();
        monitorService?.Stop();

        // Dispose smart card service
        var smartCardService = _host.Services.GetService<ISmartCardService>();
        smartCardService?.Dispose();

        // Dispose view model
        var viewModel = _host.Services.GetService<MainViewModel>();
        viewModel?.Dispose();

        await _host.StopAsync();
        _host.Dispose();

        base.OnExit(e);
    }
}
