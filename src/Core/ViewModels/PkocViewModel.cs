using CommunityToolkit.Mvvm.ComponentModel;
using CredBench.Core.Models.TechnologyDetails;

namespace CredBench.Core.ViewModels;

/// <summary>
/// State for the PKOC tab: the detected credential and its signature check.
/// Programming lives in <see cref="ProgramCardViewModel"/>.
/// </summary>
public partial class PkocViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetectedCredential))]
    [NotifyPropertyChangedFor(nameof(SignatureValidVisible))]
    [NotifyPropertyChangedFor(nameof(SignatureInvalidVisible))]
    [NotifyPropertyChangedFor(nameof(SignatureNotAvailableVisible))]
    [NotifyPropertyChangedFor(nameof(CredentialValue))]
    private PKOCDetails? _detectedDetails;

    /// <summary>How many bits of the credential to show.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CredentialValue))]
    private PkocCredentialSize _selectedCredentialSize = PkocCredentialSize.Bits256;

    /// <summary>How to write out the credential number.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CredentialValue))]
    private CredentialFormat _selectedCredentialFormat = CredentialFormat.WiegandBits;

    public bool HasDetectedCredential => DetectedDetails != null;
    public bool SignatureValidVisible => DetectedDetails?.SignatureValid == true;
    public bool SignatureInvalidVisible => DetectedDetails?.SignatureValid == false;
    public bool SignatureNotAvailableVisible => DetectedDetails != null && DetectedDetails.SignatureValid is null;

    /// <summary>The detected credential in the selected size and format.</summary>
    public string? CredentialValue => DetectedDetails?.GetCredential(SelectedCredentialSize, SelectedCredentialFormat);
}
