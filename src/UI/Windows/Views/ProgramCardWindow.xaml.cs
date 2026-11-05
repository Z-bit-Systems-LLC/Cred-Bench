using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using CredBench.Core.ViewModels;
using Microsoft.Win32;

namespace CredBench.Windows.Views;

/// <summary>
/// Program Card dialog: PKOC and/or a LEAF Verified test credential on a blank JavaCard.
/// </summary>
public partial class ProgramCardWindow
{
    private static readonly Regex HexRegex = new("^[0-9A-Fa-f]+$", RegexOptions.Compiled);
    private static readonly Regex DigitsRegex = new("^[0-9]+$", RegexOptions.Compiled);

    private readonly ProgramCardViewModel _viewModel;

    public ProgramCardWindow(ProgramCardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        Closing += OnClosing;
        Loaded += OnLoaded;
    }

    /// <summary>Shows what is already on the card as soon as the dialog opens.</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel.QueryCardContentCommand.CanExecute(null))
            _viewModel.QueryCardContentCommand.Execute(null);
    }

    /// <summary>Closing mid-write would leave a half-programmed card, so wait for the run to finish.</summary>
    private void OnClosing(object? sender, CancelEventArgs e) => e.Cancel = _viewModel.IsProgramming;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void CreateTestCa_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder() is { } folder)
            _viewModel.CreateTestCa(folder);
    }

    private void LoadTestCa_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder() is { } folder)
            _viewModel.LoadTestCa(folder);
    }

    private string? PickFolder()
    {
        var dialog = new OpenFolderDialog { InitialDirectory = _viewModel.TestCaDirectory ?? "" };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    private void OpenIdInput_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !DigitsRegex.IsMatch(e.Text);

    private void OpenIdInput_Pasting(object sender, DataObjectPastingEventArgs e) => FilterPaste(e, DigitsRegex);

    private void CustomKeyInput_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !HexRegex.IsMatch(e.Text);

    private void CustomKeyInput_Pasting(object sender, DataObjectPastingEventArgs e) => FilterPaste(e, HexRegex);

    private static void FilterPaste(DataObjectPastingEventArgs e, Regex allowed)
    {
        if (!e.DataObject.GetDataPresent(typeof(string))
            || !allowed.IsMatch(((string)e.DataObject.GetData(typeof(string))!).Trim()))
            e.CancelCommand();
    }
}
