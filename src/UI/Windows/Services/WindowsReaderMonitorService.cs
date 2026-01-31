using CredBench.Core.Services;
using PCSC;
using PCSC.Monitoring;

namespace CredBench.Windows.Services;

public class WindowsReaderMonitorService : IReaderMonitorService
{
    private readonly WindowsSmartCardService _smartCardService;
    private readonly ISCardContext _context;
    private ISCardMonitor? _monitor;
    private IDeviceMonitor? _deviceMonitor;
    private bool _disposed;

    public bool IsRunning => _monitor?.Monitoring ?? false;

    public event EventHandler<ReaderEventArgs>? CardInserted;
    public event EventHandler<ReaderEventArgs>? CardRemoved;
    public event EventHandler? ReadersChanged;

    public WindowsReaderMonitorService(WindowsSmartCardService smartCardService)
    {
        _smartCardService = smartCardService;
        _context = ContextFactory.Instance.Establish(SCardScope.System);
    }

    public void Start()
    {
        if (IsRunning)
            return;

        var readers = _smartCardService.GetReaders();

        if (readers.Count > 0)
        {
            _monitor = MonitorFactory.Instance.Create(SCardScope.System);
            _monitor.CardInserted += OnCardInserted;
            _monitor.CardRemoved += OnCardRemoved;
            _monitor.Start(readers.ToArray());
        }

        // Monitor for reader add/remove
        _deviceMonitor = DeviceMonitorFactory.Instance.Create(SCardScope.System);
        _deviceMonitor.StatusChanged += OnDeviceStatusChanged;
        _deviceMonitor.Start();
    }

    public void Stop()
    {
        _monitor?.Cancel();
        _monitor?.Dispose();
        _monitor = null;

        _deviceMonitor?.Cancel();
        _deviceMonitor?.Dispose();
        _deviceMonitor = null;
    }

    public void Restart()
    {
        Stop();
        Start();
        _smartCardService.RaiseReadersChanged();
    }

    private void OnCardInserted(object? sender, CardStatusEventArgs e)
    {
        var atr = e.Atr != null ? BitConverter.ToString(e.Atr).Replace("-", " ") : null;
        _smartCardService.RaiseCardInserted(e.ReaderName, atr);
        CardInserted?.Invoke(this, new ReaderEventArgs { ReaderName = e.ReaderName, ATR = atr });
    }

    private void OnCardRemoved(object? sender, CardStatusEventArgs e)
    {
        _smartCardService.RaiseCardRemoved(e.ReaderName);
        CardRemoved?.Invoke(this, new ReaderEventArgs { ReaderName = e.ReaderName });
    }

    private void OnDeviceStatusChanged(object? sender, DeviceChangeEventArgs e)
    {
        _smartCardService.RaiseReadersChanged();
        ReadersChanged?.Invoke(this, EventArgs.Empty);

        // Restart card monitor with updated reader list
        Stop();
        Start();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
