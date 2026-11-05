using System.IO;
using System.Windows;
using System.Windows.Controls;
using CredBench.Core.ViewModels;
using Microsoft.Win32;

namespace CredBench.Windows.Views.CardDetails;

public partial class LeafVerifiedDetailsControl : UserControl
{
    private const string CertificateFilter = "Certificates (*.crt;*.pem;*.cer;*.der)|*.crt;*.pem;*.cer;*.der|All files (*.*)|*.*";

    public LeafVerifiedDetailsControl()
    {
        InitializeComponent();
    }

    private LeafVerifiedViewModel? ViewModel => DataContext as LeafVerifiedViewModel;

    private void AddTestRoot_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        var dialog = new OpenFileDialog { Filter = CertificateFilter };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        try
        {
            viewModel.AddTestRoot(File.ReadAllBytes(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            viewModel.TrustStoreMessage = ex.Message;
        }
    }

    private void ExportCertificate_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.GetCertificatePem() is not { } pem)
            return;

        var dialog = new SaveFileDialog
        {
            Filter = CertificateFilter,
            FileName = ViewModel.SuggestedCertificateFileName,
            DefaultExt = ".crt"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        try
        {
            File.WriteAllText(dialog.FileName, pem);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViewModel.TrustStoreMessage =
                string.Format(Core.Resources.Resources.GetString("LEAFV_ExportFailed"), ex.Message);
        }
    }
}
