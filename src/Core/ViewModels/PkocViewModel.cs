using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CredBench.Core.Models.TechnologyDetails;
using CredBench.Core.Services;
using CredBench.Core.Services.GlobalPlatform;
using CredBench.Core.Services.Pkoc;

namespace CredBench.Core.ViewModels;

public partial class PkocViewModel : ObservableObject
{
    private readonly ISmartCardService _smartCardService;
    private readonly PkocCardProgrammer _programmer;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetectedCredential))]
    [NotifyPropertyChangedFor(nameof(SignatureValidVisible))]
    [NotifyPropertyChangedFor(nameof(SignatureInvalidVisible))]
    [NotifyPropertyChangedFor(nameof(SignatureNotAvailableVisible))]
    private PKOCDetails? _detectedDetails;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProgramCardCommand))]
    private bool _isProgramming;

    [ObservableProperty]
    private string? _programmingStatus;

    [ObservableProperty]
    private int _programmingProgress;

    [ObservableProperty]
    private int _programmingTotal;

    [ObservableProperty]
    private bool _useCustomKey;

    [ObservableProperty]
    private string _customKeyHex = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProgramCardCommand))]
    private string? _selectedReader;

    [ObservableProperty]
    private bool _programmingComplete;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCardContent))]
    private CardContent? _cardContent;

    [ObservableProperty]
    private bool _isQuerying;

    [ObservableProperty]
    private string? _queryError;

    [ObservableProperty]
    private string? _queryErrorDetails;

    [ObservableProperty]
    private bool _showQueryErrorDetails;

    public bool HasCardContent => CardContent != null;

    public bool HasDetectedCredential => DetectedDetails != null;
    public bool SignatureValidVisible => DetectedDetails?.SignatureValid == true;
    public bool SignatureInvalidVisible => DetectedDetails?.SignatureValid == false;
    public bool SignatureNotAvailableVisible => DetectedDetails != null && DetectedDetails.SignatureValid is null;

    public PkocViewModel(
        ISmartCardService smartCardService,
        PkocCardProgrammer programmer,
        ILocalizationService localization)
    {
        _smartCardService = smartCardService;
        _programmer = programmer;
        _localization = localization;
    }

    private bool CanProgramCard() => !IsProgramming && !string.IsNullOrEmpty(SelectedReader);

    [RelayCommand(CanExecute = nameof(CanProgramCard))]
    private async Task ProgramCardAsync()
    {
        if (string.IsNullOrEmpty(SelectedReader))
            return;

        try
        {
            IsProgramming = true;
            ProgrammingComplete = false;
            ErrorMessage = null;
            ProgrammingStatus = _localization.GetString("PKOC_Programming_SecureChannel");
            ProgrammingProgress = 0;
            ProgrammingTotal = 0;

            var capData = LoadCapFile();
            byte[]? gpKey = UseCustomKey ? ParseHexKey(CustomKeyHex) : null;

            var loadProgress = new Progress<(int Current, int Total)>(p =>
            {
                ProgrammingProgress = p.Current;
                ProgrammingTotal = p.Total;
                ProgrammingStatus = _localization.GetString("PKOC_Programming_Loading", p.Current, p.Total);
            });

            var statusProgress = new Progress<string>(status =>
            {
                ProgrammingStatus = status;
            });

            await Task.Run(() =>
            {
                using var connection = _smartCardService.Connect(SelectedReader);
                _programmer.ProgramCard(connection, capData, gpKey,
                    deleteExisting: true, loadProgress, statusProgress);
            });

            ProgrammingStatus = _localization.GetString("PKOC_Programming_Complete");
            ProgrammingComplete = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ProgrammingStatus = _localization.GetString("PKOC_Programming_Failed");
        }
        finally
        {
            IsProgramming = false;
        }
    }

    [RelayCommand]
    private void DismissError()
    {
        ErrorMessage = null;
        ProgrammingStatus = null;
    }

    [RelayCommand]
    private async Task QueryCardContentAsync()
    {
        if (string.IsNullOrEmpty(SelectedReader))
            return;

        try
        {
            IsQuerying = true;
            QueryError = null;
            QueryErrorDetails = null;
            ShowQueryErrorDetails = false;
            CardContent = null;

            byte[]? gpKey = UseCustomKey ? ParseHexKey(CustomKeyHex) : null;

            var statusProgress = new Progress<string>(status =>
            {
                ProgrammingStatus = status;
            });

            var content = await Task.Run(() =>
            {
                using var connection = _smartCardService.Connect(SelectedReader);
                return _programmer.QueryCardContent(connection, gpKey, statusProgress);
            });

            CardContent = content;
            ProgrammingStatus = null;
        }
        catch (GlobalPlatformException gpEx)
        {
            QueryError = gpEx.Message;
            QueryErrorDetails = gpEx.DiagnosticDetails;
            ProgrammingStatus = null;
        }
        catch (Exception ex)
        {
            QueryError = ex.Message;
            ProgrammingStatus = null;
        }
        finally
        {
            IsQuerying = false;
        }
    }

    [RelayCommand]
    private void DismissQueryError()
    {
        QueryError = null;
        QueryErrorDetails = null;
        ShowQueryErrorDetails = false;
    }

    [RelayCommand]
    private void ToggleQueryErrorDetails()
    {
        ShowQueryErrorDetails = !ShowQueryErrorDetails;
    }

    private static byte[] LoadCapFile()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("CredBench.Core.Resources.pkoc.cap")
            ?? throw new InvalidOperationException("PKOC applet file not found in embedded resources.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static byte[] ParseHexKey(string hex)
    {
        var cleaned = hex.Replace(" ", "").Replace("-", "");
        if (cleaned.Length != 32)
            throw new ArgumentException("GP key must be 16 bytes (32 hex characters).");

        var bytes = new byte[16];
        for (var i = 0; i < 16; i++)
            bytes[i] = Convert.ToByte(cleaned.Substring(i * 2, 2), 16);
        return bytes;
    }
}
