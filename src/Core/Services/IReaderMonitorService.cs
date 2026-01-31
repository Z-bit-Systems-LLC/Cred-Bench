namespace CredBench.Core.Services;

public interface IReaderMonitorService : IDisposable
{
    void Start();
    void Stop();
    bool IsRunning { get; }
    event EventHandler<ReaderEventArgs>? CardInserted;
    event EventHandler<ReaderEventArgs>? CardRemoved;
    event EventHandler? ReadersChanged;
}
