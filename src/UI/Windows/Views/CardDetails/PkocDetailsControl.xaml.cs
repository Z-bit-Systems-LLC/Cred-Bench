using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CredBench.Windows.Views.CardDetails;

public partial class PkocDetailsControl : UserControl
{
    private static readonly Regex HexRegex = new("^[0-9A-Fa-f]+$", RegexOptions.Compiled);

    public PkocDetailsControl()
    {
        InitializeComponent();
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
