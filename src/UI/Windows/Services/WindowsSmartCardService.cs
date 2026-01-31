using CredBench.Core.Services;
using PCSC;
using PCSC.Exceptions;

namespace CredBench.Windows.Services;

public class WindowsSmartCardService : ISmartCardService
{
    private readonly ISCardContext _context;
    private bool _disposed;

    public event EventHandler<ReaderEventArgs>? CardInserted;
    public event EventHandler<ReaderEventArgs>? CardRemoved;
    public event EventHandler? ReadersChanged;

    public WindowsSmartCardService()
    {
        _context = ContextFactory.Instance.Establish(SCardScope.System);
    }

    public IReadOnlyList<string> GetReaders()
    {
        try
        {
            return _context.GetReaders()?.ToList() ?? [];
        }
        catch (PCSCException)
        {
            return [];
        }
    }

    public ICardConnection Connect(string readerName)
    {
        var reader = _context.ConnectReader(readerName, SCardShareMode.Shared, SCardProtocol.Any);
        return new CardConnection(readerName, reader);
    }

    internal void RaiseCardInserted(string readerName, string? atr)
    {
        CardInserted?.Invoke(this, new ReaderEventArgs { ReaderName = readerName, ATR = atr });
    }

    internal void RaiseCardRemoved(string readerName)
    {
        CardRemoved?.Invoke(this, new ReaderEventArgs { ReaderName = readerName });
    }

    internal void RaiseReadersChanged()
    {
        ReadersChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}

internal sealed class CardConnection : ICardConnection
{
    private readonly ICardReader _reader;
    private bool _disposed;

    public string ReaderName { get; }

    public CardConnection(string readerName, ICardReader reader)
    {
        ReaderName = readerName;
        _reader = reader;
    }

    public string? GetATR()
    {
        try
        {
            var atr = _reader.GetAttrib(SCardAttribute.AtrString);
            return atr != null ? BitConverter.ToString(atr).Replace("-", " ") : null;
        }
        catch
        {
            return null;
        }
    }

    public string? GetUID()
    {
        try
        {
            // GET UID command (PC/SC pseudo-APDU)
            byte[] getUidCommand = [0xFF, 0xCA, 0x00, 0x00, 0x00];
            var response = Transmit(getUidCommand);

            if (response.Length >= 2)
            {
                var sw1 = response[^2];
                var sw2 = response[^1];

                if (sw1 == 0x90 && sw2 == 0x00 && response.Length > 2)
                {
                    var uid = response[..^2];
                    return BitConverter.ToString(uid).Replace("-", " ");
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public byte[] Transmit(byte[] command)
    {
        var receiveBuffer = new byte[256];
        var bytesReceived = _reader.Transmit(command, receiveBuffer);

        var response = new byte[bytesReceived];
        Array.Copy(receiveBuffer, response, bytesReceived);
        return response;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _reader.Dispose();
    }
}
