using System.Collections.ObjectModel;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CredBench.Core.Models.TechnologyDetails;
using CredBench.Core.Services;
using CredBench.Core.Services.CardDetectors;
using CredBench.Core.Services.Leaf;

namespace CredBench.Core.ViewModels;

/// <summary>
/// State for the LEAF Verified tab: the detected credential, its trust verdict,
/// and the trust anchors (built-in NXP root plus user-added test roots).
/// Programming lives in <see cref="ProgramCardViewModel"/>.
/// </summary>
public partial class LeafVerifiedViewModel : ObservableObject
{
    private readonly LeafTrustStore _trustStore;
    private readonly LeafPrintedIdLookup _printedIdLookup;
    private readonly ILocalizationService _localization;
    private readonly SynchronizationContext? _syncContext;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetectedCredential))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(ShowVerified))]
    [NotifyPropertyChangedFor(nameof(ShowWarning))]
    [NotifyPropertyChangedFor(nameof(ShowFailure))]
    [NotifyPropertyChangedFor(nameof(CardSignatureText))]
    [NotifyPropertyChangedFor(nameof(CertificateText))]
    [NotifyPropertyChangedFor(nameof(HasProfileIssues))]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(CanExportCertificate))]
    [NotifyPropertyChangedFor(nameof(OpenIdQualifier))]
    [NotifyPropertyChangedFor(nameof(HasTapUrl))]
    [NotifyPropertyChangedFor(nameof(HasPrintedIdNote))]
    [NotifyPropertyChangedFor(nameof(HasPrintedId))]
    [NotifyPropertyChangedFor(nameof(ShowLookUpPrintedId))]
    [NotifyCanExecuteChangedFor(nameof(LookUpPrintedIdCommand))]
    private LEAFVerifiedDetails? _detectedDetails;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LookUpPrintedIdCommand))]
    private bool _isLookingUpPrintedId;

    [ObservableProperty]
    private string? _trustStoreMessage;

    public ObservableCollection<LeafTrustAnchor> TrustAnchors { get; } = [];

    public bool HasDetectedCredential => DetectedDetails is not null;
    public bool ShowVerified => DetectedDetails?.IsFullyVerified == true;
    public bool ShowFailure => DetectedDetails?.CardSignatureValid == false;
    public bool ShowWarning => DetectedDetails is not null && !ShowVerified && !ShowFailure;
    public bool HasProfileIssues => DetectedDetails?.ProfileIssues.Count > 0;
    public bool HasError => DetectedDetails?.Error is not null;
    public bool CanExportCertificate => DetectedDetails?.Certificate is { Length: > 0 };
    public bool HasTapUrl => DetectedDetails?.TapUrl is not null;
    public bool HasPrintedIdNote => DetectedDetails?.PrintedIdNote is not null;
    public bool HasPrintedId => DetectedDetails?.PrintedId is not null;

    /// <summary>
    /// The lookup is offered once per scan: the tap URL is single-use, so after any attempt
    /// (success or failure) the button goes until the card is scanned again.
    /// </summary>
    public bool ShowLookUpPrintedId => DetectedDetails is { TapUrl: not null, PrintedId: null, PrintedIdNote: null };

    /// <summary>"(unverified)" next to the Open ID unless both signatures checked out.</summary>
    public string OpenIdQualifier => ShowVerified || DetectedDetails is null
        ? ""
        : _localization.GetString("LEAFV_Unverified");

    public string StatusText
    {
        get
        {
            if (DetectedDetails is not { } d)
                return "";
            if (d.IsFullyVerified)
                return _localization.GetString("LEAFV_Status_Verified", d.TrustAnchorName ?? "");
            if (d.CardSignatureValid == false)
                return _localization.GetString("LEAFV_Status_CloneSuspect");
            return d.Trust switch
            {
                LeafTrust.UntrustedIssuer => _localization.GetString("LEAFV_Status_Untrusted", d.Issuer ?? "?"),
                LeafTrust.ProfileViolation => _localization.GetString("LEAFV_Status_ProfileViolation"),
                LeafTrust.Unreadable => _localization.GetString("LEAFV_Status_Unreadable"),
                _ => _localization.GetString("LEAFV_Status_ChallengeIncomplete")
            };
        }
    }

    public string CertificateText => DetectedDetails?.Trust switch
    {
        LeafTrust.Verified => _localization.GetString("LEAFV_Cert_Trusted", DetectedDetails.TrustAnchorName ?? ""),
        LeafTrust.UntrustedIssuer => _localization.GetString("LEAFV_Cert_Untrusted"),
        LeafTrust.ProfileViolation when DetectedDetails.TrustAnchorName is { } anchor =>
            _localization.GetString("LEAFV_Cert_ProfileViolationTrusted", anchor),
        LeafTrust.ProfileViolation => _localization.GetString("LEAFV_Cert_ProfileViolation"),
        LeafTrust.Unreadable => _localization.GetString("LEAFV_Cert_Unreadable"),
        _ => ""
    };

    public string CardSignatureText => DetectedDetails?.CardSignatureValid switch
    {
        true => _localization.GetString("LEAFV_CardSig_Valid"),
        false => _localization.GetString("LEAFV_CardSig_Invalid"),
        null => _localization.GetString("LEAFV_CardSig_NotChecked")
    };

    public LeafVerifiedViewModel(
        LeafTrustStore trustStore,
        LeafPrintedIdLookup printedIdLookup,
        ILocalizationService localization)
    {
        _trustStore = trustStore;
        _printedIdLookup = printedIdLookup;
        _localization = localization;
        _syncContext = SynchronizationContext.Current;

        RefreshAnchors();
        _trustStore.Changed += (_, _) => RunOnUiThread(OnTrustStoreChanged);
    }

    /// <summary>
    /// Trusts a test root loaded from a certificate file (PEM or DER).
    /// </summary>
    public void AddTestRoot(byte[] certificateFile)
    {
        try
        {
            var anchor = _trustStore.AddTestRoot(certificateFile);
            TrustStoreMessage = _localization.GetString("LEAFV_TrustStore_Added", anchor.Name);
        }
        catch (ArgumentException ex)
        {
            TrustStoreMessage = _localization.GetString("LEAFV_TrustStore_AddFailed", ex.Message);
        }
    }

    // The tap URL is single-use, so one successful lookup per scan
    private bool CanLookUpPrintedId() => !IsLookingUpPrintedId && ShowLookUpPrintedId;

    /// <summary>
    /// Opt-in: sends the card's one-time tap URL to the LEAF Community service to learn the Printed ID.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLookUpPrintedId))]
    private async Task LookUpPrintedIdAsync()
    {
        if (DetectedDetails is not { TapUrl: { } tapUrl } details)
            return;

        try
        {
            IsLookingUpPrintedId = true;
            var result = await _printedIdLookup.LookupAsync(tapUrl);

            // Only apply it if the same card is still displayed
            if (ReferenceEquals(DetectedDetails, details))
                DetectedDetails = LeafPrintedIdLookup.Apply(details, result);
        }
        finally
        {
            IsLookingUpPrintedId = false;
        }
    }

    [RelayCommand]
    private void RemoveTestRoot(LeafTrustAnchor? anchor)
    {
        if (anchor is not null && _trustStore.RemoveTestRoot(anchor))
            TrustStoreMessage = _localization.GetString("LEAFV_TrustStore_Removed", anchor.Name);
    }

    /// <summary>The detected certificate as PEM, for export.</summary>
    public string? GetCertificatePem() =>
        DetectedDetails?.Certificate is { Length: > 0 } der
            ? PemEncoding.WriteString("CERTIFICATE", der)
            : null;

    /// <summary>Suggested file name for the exported certificate.</summary>
    public string SuggestedCertificateFileName =>
        DetectedDetails?.OpenId is { } id ? $"leaf-{id}.crt" : "leaf-card.crt";

    private void OnTrustStoreChanged()
    {
        RefreshAnchors();

        // The issuer check is pure computation on the stored certificate, so a new test
        // root applies at once. The card signature result does not depend on the anchors.
        if (DetectedDetails is { Certificate: { } certificate, Trust: not LeafTrust.Unreadable })
        {
            var reevaluated = LEAFVerifiedDetector.EvaluateCertificate(certificate, _trustStore.Anchors);
            DetectedDetails = DetectedDetails with
            {
                Trust = reevaluated.Trust,
                TrustAnchorName = reevaluated.TrustAnchorName
            };
        }
    }

    private void RefreshAnchors()
    {
        TrustAnchors.Clear();
        foreach (var anchor in _trustStore.Anchors)
            TrustAnchors.Add(anchor);
    }

    private void RunOnUiThread(Action action)
    {
        if (_syncContext is not null && SynchronizationContext.Current != _syncContext)
            _syncContext.Post(_ => action(), null);
        else
            action();
    }
}
