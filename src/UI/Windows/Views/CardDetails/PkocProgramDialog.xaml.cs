using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using CredBench.Core.ViewModels;

namespace CredBench.Windows.Views.CardDetails;

public partial class PkocProgramDialog
{
    private static readonly Regex HexRegex = new("^[0-9A-Fa-f]+$", RegexOptions.Compiled);

    public PkocProgramDialog(PkocViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PkocViewModel vm && vm.QueryCardContentCommand.CanExecute(null))
            vm.QueryCardContentCommand.Execute(null);
    }

    private void CustomKeyInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !HexRegex.IsMatch(e.Text);
    }

    private void CustomKeyInput_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(typeof(string)))
        {
            var text = (string)e.DataObject.GetData(typeof(string))!;
            if (!HexRegex.IsMatch(text))
                e.CancelCommand();
        }
        else
        {
            e.CancelCommand();
        }
    }
}
