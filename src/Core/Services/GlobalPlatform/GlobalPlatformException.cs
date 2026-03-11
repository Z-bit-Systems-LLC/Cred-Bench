namespace CredBench.Core.Services.GlobalPlatform;

public class GlobalPlatformException : Exception
{
    public byte Sw1 { get; }
    public byte Sw2 { get; }
    public ushort StatusWord => (ushort)((Sw1 << 8) | Sw2);

    public GlobalPlatformException(byte sw1, byte sw2)
        : base($"GlobalPlatform error: SW={sw1:X2}{sw2:X2}")
    {
        Sw1 = sw1;
        Sw2 = sw2;
    }

    public GlobalPlatformException(string message) : base(message) { }
    public GlobalPlatformException(string message, Exception inner) : base(message, inner) { }
}
