using System.ComponentModel;
using CredBench.Core.ViewModels;
using ZBitSystems.Wpf.UI.Services;
using ZBitSystems.Wpf.UI.Settings;

namespace CredBench.Windows.Views;

public partial class MainWindow
{
    private readonly IUserSettingsService<UserSettings> _settingsService;
    private readonly WindowStateManager _windowStateManager;

    public MainWindow(MainViewModel viewModel, IUserSettingsService<UserSettings> settingsService)
    {
        InitializeComponent();
        DataContext = viewModel;

        _settingsService = settingsService;
        _windowStateManager = new WindowStateManager(this, settingsService.Settings);
        _windowStateManager.RestoreWindowState();
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        _windowStateManager.SaveWindowState();
        await _settingsService.SaveAsync();
    }
}
