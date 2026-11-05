using System.ComponentModel;
using System.Windows;
using CredBench.Core.ViewModels;
using ZBitSystems.Wpf.UI.Services;
using ZBitSystems.Wpf.UI.Settings;

namespace CredBench.Windows.Views;

public partial class MainWindow
{
    private readonly IUserSettingsService<UserSettings> _settingsService;
    private readonly WindowStateManager _windowStateManager;
    private readonly ThemeManager _themeManager;
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel, IUserSettingsService<UserSettings> settingsService)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;

        _settingsService = settingsService;
        _windowStateManager = new WindowStateManager(this, settingsService.Settings);
        _windowStateManager.RestoreWindowState();
        _themeManager = new ThemeManager(this);
        Closing += OnClosing;
    }

    private void ProgramCard_Click(object sender, RoutedEventArgs e)
    {
        new ProgramCardWindow(_viewModel.ProgramCardViewModel) { Owner = this }.ShowDialog();
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        _themeManager.Dispose();
        _windowStateManager.SaveWindowState();
        await _settingsService.SaveAsync();
    }
}
