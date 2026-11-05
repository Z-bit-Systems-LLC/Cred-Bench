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
    private PKOCDetails? _detectedDetails;

    public bool HasDetectedCredential => DetectedDetails != null;
    public bool SignatureValidVisible => DetectedDetails?.SignatureValid == true;
    public bool SignatureInvalidVisible => DetectedDetails?.SignatureValid == false;
    public bool SignatureNotAvailableVisible => DetectedDetails != null && DetectedDetails.SignatureValid is null;
}
