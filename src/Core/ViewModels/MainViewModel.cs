using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CredBench.Core.Models;
using CredBench.Core.Services;

namespace CredBench.Core.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ISmartCardService _smartCardService;
    private readonly IReaderMonitorService _readerMonitorService;
    private readonly CardDetectionService _detectionService;
    private readonly SynchronizationContext? _syncContext;
    private CancellationTokenSource? _scanCts;
    private bool _disposed;

    [ObservableProperty]
    private ObservableCollection<string> _availableReaders = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCardCommand))]
    private string? _selectedReader;

    [ObservableProperty]
    private DetectionResult? _currentResult;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCardCommand))]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _hasPIV;

    [ObservableProperty]
    private bool _hasDESFire;

    [ObservableProperty]
    private bool _hasISO14443;

    [ObservableProperty]
    private bool _hasPKOC;

    [ObservableProperty]
    private bool _hasLEAF;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private bool _isScanningPIV;

    [ObservableProperty]
    private bool _isScanningDESFire;

    [ObservableProperty]
    private bool _isScanningISO14443;

    [ObservableProperty]
    private bool _isScanningPKOC;

    [ObservableProperty]
    private bool _isScanningLEAF;

    public MainViewModel(
        ISmartCardService smartCardService,
        IReaderMonitorService readerMonitorService,
        CardDetectionService detectionService)
    {
        _smartCardService = smartCardService;
        _readerMonitorService = readerMonitorService;
        _detectionService = detectionService;
        _syncContext = SynchronizationContext.Current;

        _smartCardService.CardInserted += OnCardInserted;
        _smartCardService.CardRemoved += OnCardRemoved;
        _smartCardService.ReadersChanged += OnReadersChanged;

        RefreshReaders();
    }

    partial void OnCurrentResultChanged(DetectionResult? value)
    {
        HasResult = value != null;
        HasPIV = value?.HasTechnology(CardTechnology.PIV) ?? false;
        HasDESFire = value?.HasTechnology(CardTechnology.DESFire) ?? false;
        HasISO14443 = value?.HasTechnology(CardTechnology.ISO14443) ?? false;
        HasPKOC = value?.HasTechnology(CardTechnology.PKOC) ?? false;
        HasLEAF = value?.HasTechnology(CardTechnology.LEAF) ?? false;
    }

    [RelayCommand]
    private void RefreshReaders()
    {
        var readers = _smartCardService.GetReaders();
        AvailableReaders.Clear();

        foreach (var reader in readers)
        {
            AvailableReaders.Add(reader);
        }

        if (AvailableReaders.Count > 0 && string.IsNullOrEmpty(SelectedReader))
        {
            SelectedReader = AvailableReaders[0];
        }

        StatusMessage = AvailableReaders.Count > 0
            ? $"Found {AvailableReaders.Count} reader(s)"
            : "No readers found";
    }

    private bool CanScanCard() => !IsScanning && !string.IsNullOrEmpty(SelectedReader);

    [RelayCommand(CanExecute = nameof(CanScanCard))]
    private async Task ScanCardAsync()
    {
        if (string.IsNullOrEmpty(SelectedReader))
            return;

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();

        try
        {
            IsScanning = true;
            StatusMessage = "Scanning card...";
            CurrentResult = null;
            ClearScanningStates();

            var progress = new Progress<CardTechnology>(OnDetectionProgress);
            var result = await _detectionService.DetectAsync(SelectedReader, progress, _scanCts.Token);
            CurrentResult = result;

            if (result.Technologies == CardTechnology.Unknown)
            {
                StatusMessage = "Card detected but technology unknown";
            }
            else
            {
                var technologies = GetTechnologyNames(result.Technologies);
                StatusMessage = $"Detected: {string.Join(", ", technologies)}";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Scan cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            ClearScanningStates();
        }
    }

    private void OnDetectionProgress(CardTechnology technology)
    {
        RunOnUiThread(() =>
        {
            ClearScanningStates();
            switch (technology)
            {
                case CardTechnology.PIV:
                    IsScanningPIV = true;
                    break;
                case CardTechnology.DESFire:
                    IsScanningDESFire = true;
                    break;
                case CardTechnology.ISO14443:
                    IsScanningISO14443 = true;
                    break;
                case CardTechnology.PKOC:
                    IsScanningPKOC = true;
                    break;
                case CardTechnology.LEAF:
                    IsScanningLEAF = true;
                    break;
            }
        });
    }

    private void ClearScanningStates()
    {
        IsScanningPIV = false;
        IsScanningDESFire = false;
        IsScanningISO14443 = false;
        IsScanningPKOC = false;
        IsScanningLEAF = false;
    }

    [RelayCommand]
    private void ClearResults()
    {
        CurrentResult = null;
        StatusMessage = "Ready";
    }

    [RelayCommand]
    private void ResetReader()
    {
        _scanCts?.Cancel();
        CurrentResult = null;
        StatusMessage = "Resetting reader...";

        _readerMonitorService.Restart();

        RefreshReaders();
        StatusMessage = "Reader reset complete";
    }

    private void OnCardInserted(object? sender, ReaderEventArgs e)
    {
        if (e.ReaderName == SelectedReader)
        {
            RunOnUiThread(() =>
            {
                StatusMessage = "Card inserted - scanning...";
                ScanCardCommand.Execute(null);
            });
        }
    }

    private void OnCardRemoved(object? sender, ReaderEventArgs e)
    {
        if (e.ReaderName == SelectedReader)
        {
            RunOnUiThread(() =>
            {
                _scanCts?.Cancel();
                StatusMessage = "Card removed";
            });
        }
    }

    private void OnReadersChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(RefreshReaders);
    }

    private void RunOnUiThread(Action action)
    {
        if (_syncContext != null)
        {
            _syncContext.Post(_ => action(), null);
        }
        else
        {
            action();
        }
    }

    private static IEnumerable<string> GetTechnologyNames(CardTechnology technologies)
    {
        if (technologies.HasFlag(CardTechnology.PIV))
            yield return "PIV";
        if (technologies.HasFlag(CardTechnology.DESFire))
            yield return "DESFire";
        if (technologies.HasFlag(CardTechnology.ISO14443))
            yield return "ISO14443";
        if (technologies.HasFlag(CardTechnology.PKOC))
            yield return "PKOC";
        if (technologies.HasFlag(CardTechnology.LEAF))
            yield return "LEAF";
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _scanCts?.Cancel();
        _scanCts?.Dispose();

        _smartCardService.CardInserted -= OnCardInserted;
        _smartCardService.CardRemoved -= OnCardRemoved;
        _smartCardService.ReadersChanged -= OnReadersChanged;

        GC.SuppressFinalize(this);
    }
}
