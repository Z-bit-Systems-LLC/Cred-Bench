using System.Windows;
using CredBench.Core.Services;
using CredBench.Core.Services.CardDetectors;
using CredBench.Core.ViewModels;
using CredBench.Windows.Services;
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
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                // Settings
                services.AddSingleton<IUserSettingsService<UserSettings>>(
                    new JsonUserSettingsService<UserSettings>("CredBench"));

                // Smart card services
                services.AddSingleton<WindowsSmartCardService>();
                services.AddSingleton<ISmartCardService>(sp => sp.GetRequiredService<WindowsSmartCardService>());
                services.AddSingleton<IReaderMonitorService, WindowsReaderMonitorService>();

                // Card detectors
                services.AddSingleton<ICardDetector, PIVDetector>();
                services.AddSingleton<ICardDetector, DESFireDetector>();
                services.AddSingleton<ICardDetector, ISO14443Detector>();
                services.AddSingleton<ICardDetector, PKOCDetector>();
                services.AddSingleton<ICardDetector, LEAFDetector>();

                // Detection service
                services.AddSingleton<CardDetectionService>();

                // ViewModels
                services.AddSingleton<MainViewModel>();

                // Views
                services.AddSingleton<MainWindow>();
            })
            .Build();
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
