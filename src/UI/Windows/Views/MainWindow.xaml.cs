using CredBench.Core.ViewModels;
using Wpf.Ui.Controls;

namespace CredBench.Windows.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
