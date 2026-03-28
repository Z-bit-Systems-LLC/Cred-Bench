namespace CredBench.Core.Services.GlobalPlatform;

public class GlobalPlatformException : Exception
{
    public byte Sw1 { get; }
    public byte Sw2 { get; }
    public ushort StatusWord => (ushort)((Sw1 << 8) | Sw2);

    public GlobalPlatformException(byte sw1, byte sw2)
        : base(FormatMessage(sw1, sw2))
    {
        Sw1 = sw1;
        Sw2 = sw2;
    }

    private static string FormatMessage(byte sw1, byte sw2)
    {
        var description = (sw1, sw2) switch
        {
            (0x6A, 0x82) => "Card does not support GlobalPlatform (applet not found)",
            (0x6A, 0x86) => "Incorrect parameters (P1/P2)",
            (0x6A, 0x80) => "Incorrect data field",
            (0x69, 0x82) => "Security status not satisfied",
            (0x69, 0x85) => "Conditions of use not satisfied",
            (0x63, 0x00) => "Authentication failed (wrong key)",
            (0x6E, 0x00) => "Class not supported",
            (0x6D, 0x00) => "Instruction not supported",
            (0x6A, 0x88) => "Referenced data not found",
            (0x6A, 0x84) => "Not enough memory space",
            _ when sw1 == 0x63 => $"Authentication failed (SW={sw1:X2}{sw2:X2})",
            _ => $"Unexpected error (SW={sw1:X2}{sw2:X2})",
        };
        return description;
    }

    /// <summary>Optional diagnostic details (hex dumps, cryptogram values, etc.).</summary>
    public string? DiagnosticDetails { get; }

    public GlobalPlatformException(string message) : base(message) { }
    public GlobalPlatformException(string message, string diagnosticDetails) : base(message)
    {
        DiagnosticDetails = diagnosticDetails;
    }
    public GlobalPlatformException(string message, Exception inner) : base(message, inner) { }
}
