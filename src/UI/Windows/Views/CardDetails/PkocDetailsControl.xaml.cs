using System.Windows;
using System.Windows.Controls;
using CredBench.Core.ViewModels;

namespace CredBench.Windows.Views.CardDetails;

public partial class PkocDetailsControl : UserControl
{
    public PkocDetailsControl()
    {
        InitializeComponent();
    }

    private void OnProgramPkocClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PkocViewModel viewModel)
            return;

        var dialog = new PkocProgramDialog(viewModel)
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
    }
}
