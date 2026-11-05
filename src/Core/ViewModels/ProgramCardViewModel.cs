using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CredBench.Core.Models.TechnologyDetails;
using CredBench.Core.Services;
using CredBench.Core.Services.GlobalPlatform;
using CredBench.Core.Services.Leaf;
using CredBench.Core.Services.Pkoc;

namespace CredBench.Core.ViewModels;

/// <summary>
/// The Program Card dialog: installs PKOC and/or a LEAF Verified test credential on a blank JavaCard
/// in one run. PKOC goes first so that LEAF ends up as the default-selected applet.
/// </summary>
public partial class ProgramCardViewModel : ObservableObject
{
    /// <summary>Random Open IDs are drawn from the 12-digit part of the range.</summary>
    private const long RandomOpenIdMin = 100_000_000_000;

    private readonly ISmartCardService _smartCardService;
    private readonly PkocCardProgrammer _pkocProgrammer;
    private readonly LeafCardProgrammer _leafProgrammer;
    private readonly LeafTrustStore _trustStore;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProgramCardCommand))]
    private string? _selectedReader;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgramBoth))]
    [NotifyCanExecuteChangedFor(nameof(ProgramCardCommand))]
    private bool _programPkoc = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgramBoth))]
    [NotifyCanExecuteChangedFor(nameof(ProgramCardCommand))]
    private bool _programLeaf = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProgramCardCommand))]
    private bool _isProgramming;

    [ObservableProperty]
    private string? _programmingStatus;

    [ObservableProperty]
    private bool _useCustomKey;

    [ObservableProperty]
    private string _customKeyHex = "";

    [ObservableProperty]
    private string? _programmingResult;

    [ObservableProperty]
    private string? _programmingWarning;

    [ObservableProperty]
    private string? _programmingError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTestCa))]
    [NotifyPropertyChangedFor(nameof(TestCaName))]
    [NotifyCanExecuteChangedFor(nameof(ProgramCardCommand))]
    private LeafTestCa? _testCa;

    [ObservableProperty]
    private string? _testCaDirectory;

    [ObservableProperty]
    private string? _testCaMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpenIdPreview))]
    [NotifyPropertyChangedFor(nameof(OpenIdError))]
    [NotifyCanExecuteChangedFor(nameof(ProgramCardCommand))]
    private string _openIdText = "";

    public ProgramCardViewModel(
        ISmartCardService smartCardService,
        PkocCardProgrammer pkocProgrammer,
        LeafCardProgrammer leafProgrammer,
        LeafTrustStore trustStore,
        ILocalizationService localization)
    {
        _smartCardService = smartCardService;
        _pkocProgrammer = pkocProgrammer;
        _leafProgrammer = leafProgrammer;
        _trustStore = trustStore;
        _localization = localization;
    }

    public bool ProgramBoth => ProgramPkoc && ProgramLeaf;
    public bool HasTestCa => TestCa is not null;
    public string? TestCaName => TestCa?.Name;

    /// <summary>The Open ID typed in, or null when it is not 1-12 digits within 38 bits.</summary>
    public long? ParsedOpenId =>
        OpenIdText.Trim() is { Length: >= 1 and <= 12 } text
        && text.All(char.IsAsciiDigit)
        && long.Parse(text) is var id and <= LEAFVerifiedDetails.OpenIdMax
            ? id
            : null;

    public string? OpenIdPreview => ParsedOpenId is { } id
        ? _localization.GetString("LEAFV_Prog_W40Preview", LEAFVerifiedDetails.ToW40(id).ToString("X10"))
        : null;

    public string? OpenIdError => OpenIdText.Trim().Length > 0 && ParsedOpenId is null
        ? _localization.GetString("LEAFV_Prog_OpenIdInvalid")
        : null;

    partial void OnTestCaChanged(LeafTestCa? oldValue, LeafTestCa? newValue)
    {
        if (!ReferenceEquals(oldValue, newValue))
            oldValue?.Dispose();
    }

    [RelayCommand]
    private void RandomOpenId()
    {
        OpenIdText = Random.Shared.NextInt64(RandomOpenIdMin, LEAFVerifiedDetails.OpenIdMax + 1).ToString();
    }

    /// <summary>
    /// Creates a test root in <paramref name="directory"/> (refusing to overwrite one) and uses it.
    /// </summary>
    public void CreateTestCa(string directory)
    {
        var ca = LeafTestCa.Create();
        try
        {
            ca.Save(directory);
        }
        catch (IOException) when (File.Exists(Path.Combine(directory, LeafTestCa.KeyFileName))
                                  || File.Exists(Path.Combine(directory, LeafTestCa.CertificateFileName)))
        {
            ca.Dispose();
            TestCaMessage = _localization.GetString("LEAFV_Prog_TestRootExists", directory);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ca.Dispose();
            TestCaMessage = _localization.GetString("LEAFV_Prog_TestRootFailed", ex.Message);
            return;
        }

        UseTestCa(ca, directory);
        TestCaMessage = _localization.GetString("LEAFV_Prog_TestRootCreated", ca.Name, directory);
    }

    /// <summary>
    /// Loads the test root in <paramref name="directory"/> and uses it.
    /// </summary>
    public void LoadTestCa(string directory)
    {
        if (TryLoad(directory, out var error) is { } ca)
        {
            UseTestCa(ca, directory);
            TestCaMessage = _localization.GetString("LEAFV_Prog_TestRootLoaded", ca.Name);
        }
        else
        {
            TestCaMessage = _localization.GetString("LEAFV_Prog_TestRootFailed", error!);
        }
    }

    /// <summary>
    /// Restores the test root used last time, quietly: a moved or deleted folder just leaves none loaded.
    /// </summary>
    public void RestoreTestCa(string? directory)
    {
        if (!string.IsNullOrEmpty(directory) && TryLoad(directory, out _) is { } ca)
            UseTestCa(ca, directory);
    }

    private static LeafTestCa? TryLoad(string directory, out string? error)
    {
        try
        {
            error = null;
            return LeafTestCa.Load(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>Uses <paramref name="ca"/> for programming and trusts it, so programmed cards scan as Verified.</summary>
    private void UseTestCa(LeafTestCa ca, string directory)
    {
        TestCa = ca;
        TestCaDirectory = directory;
        _trustStore.AddTestRoot(ca.Certificate.RawData);
    }

    private bool CanProgramCard() =>
        !IsProgramming
        && !string.IsNullOrEmpty(SelectedReader)
        && (ProgramPkoc || ProgramLeaf)
        && (!ProgramLeaf || (TestCa is not null && ParsedOpenId is not null));

    [RelayCommand(CanExecute = nameof(CanProgramCard))]
    private async Task ProgramCardAsync()
    {
        if (SelectedReader is not { Length: > 0 } reader)
            return;

        // Snapshot the choices: the dialog stays interactive while the card is written
        var programPkoc = ProgramPkoc;
        var leafCa = ProgramLeaf ? TestCa : null;
        var openId = ParsedOpenId ?? 0;
        var pkocName = _localization.GetString("Tab_PKOC");
        var leafName = _localization.GetString("Tab_LEAFVerified");
        var stage = pkocName;
        var pkocDone = false;

        try
        {
            IsProgramming = true;
            ProgrammingResult = null;
            ProgrammingWarning = null;
            ProgrammingError = null;
            ProgrammingStatus = _localization.GetString("PKOC_Programming_SecureChannel");

            var gpKey = UseCustomKey ? GpSessionFactory.ParseKey(CustomKeyHex) : null;
            var loadProgress = new Progress<(int Current, int Total)>(p =>
                ProgrammingStatus = $"{stage}: {_localization.GetString("PKOC_Programming_Loading", p.Current, p.Total)}");
            var statusProgress = new Progress<string>(status => ProgrammingStatus = $"{stage}: {status}");

            var leafResult = await Task.Run(() =>
            {
                using var connection = _smartCardService.Connect(reader);

                if (programPkoc)
                {
                    stage = pkocName;
                    _pkocProgrammer.ProgramCard(connection, LoadCapFile("pkoc.cap"), gpKey,
                        deleteExisting: true, loadProgress, statusProgress);
                    pkocDone = true;
                }

                if (leafCa is null)
                    return null;

                stage = leafName;
                return _leafProgrammer.ProgramCard(connection, LoadCapFile("leaf.cap"), leafCa, openId,
                    gpKey, loadProgress, statusProgress);
            });

            ProgrammingStatus = null;
            List<string> done = [];
            if (pkocDone)
                done.Add(_localization.GetString("Prog_Done_Pkoc"));

            if (leafResult is not null)
            {
                if (leafResult.ReadBack.IsFullyVerified)
                    done.Add(_localization.GetString("Prog_Done_Leaf",
                        leafResult.OpenId, LEAFVerifiedDetails.ToW40(leafResult.OpenId).ToString("X10")));
                else
                    ProgrammingError = _localization.GetString("LEAFV_Prog_ReadBackFailed",
                        leafResult.ReadBack.Error ?? leafResult.ReadBack.Trust.ToString());

                if (!leafResult.ImplicitSelectionParameterAccepted)
                    ProgrammingWarning = _localization.GetString("LEAFV_Prog_NoImplicitSelection");
            }

            if (done.Count > 0)
                ProgrammingResult = string.Join(Environment.NewLine, [.. done, _localization.GetString("Prog_Done_Scan")]);
        }
        catch (Exception ex)
        {
            ProgrammingStatus = _localization.GetString("PKOC_Programming_Failed");
            ProgrammingError = $"{stage}: {ex.Message}";
            if (pkocDone)
                ProgrammingResult = _localization.GetString("Prog_Done_Pkoc");
        }
        finally
        {
            IsProgramming = false;
        }
    }

    [RelayCommand]
    private void DismissProgrammingError()
    {
        ProgrammingError = null;
        ProgrammingStatus = null;
    }

    private static byte[] LoadCapFile(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream($"CredBench.Core.Resources.{name}")
            ?? throw new InvalidOperationException($"{name} not found in embedded resources.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
