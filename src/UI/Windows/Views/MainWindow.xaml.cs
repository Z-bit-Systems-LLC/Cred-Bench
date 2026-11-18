using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closing += OnClosing;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The tab bindings have already followed the new result. A tab the new card doesn't have
        // stays selected with its header hidden, so fall back to General.
        if (e.PropertyName == nameof(MainViewModel.CurrentResult)
            && _viewModel.CurrentResult != null
            && DetailsTabControl.SelectedItem is TabItem { Visibility: not Visibility.Visible })
        {
            DetailsTabControl.SelectedIndex = 0;
        }
    }

    private void ProgramCard_Click(object sender, RoutedEventArgs e)
    {
        new ProgramCardWindow(_viewModel.ProgramCardViewModel) { Owner = this }.ShowDialog();
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _themeManager.Dispose();
        _windowStateManager.SaveWindowState();
        await _settingsService.SaveAsync();
    }
}
