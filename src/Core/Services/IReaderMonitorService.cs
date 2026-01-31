namespace CredBench.Core.Services;

public interface IReaderMonitorService : IDisposable
{
    void Start();
    void Stop();
    void Restart();
    bool IsRunning { get; }
    event EventHandler<ReaderEventArgs>? CardInserted;
    event EventHandler<ReaderEventArgs>? CardRemoved;
    event EventHandler? ReadersChanged;
}
