namespace CredBench.Core.Services;

public interface ISmartCardService : IDisposable
{
    IReadOnlyList<string> GetReaders();

    ICardConnection Connect(string readerName);

    event EventHandler<ReaderEventArgs>? CardInserted;
    event EventHandler<ReaderEventArgs>? CardRemoved;
    event EventHandler? ReadersChanged;
}

public interface ICardConnection : IDisposable
{
    string ReaderName { get; }
    string? GetATR();
    string? GetUID();
    string? GetProtocol();
    byte[] Transmit(byte[] command);
}
