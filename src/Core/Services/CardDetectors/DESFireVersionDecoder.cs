using CredBench.Core.Models.TechnologyDetails;

namespace CredBench.Core.Services.CardDetectors;

/// <summary>
/// Decodes the payload of the DESFire GetVersion command (0x60) into <see cref="DESFireDetails"/>.
/// </summary>
/// <remarks>
/// GetVersion needs no authentication and returns three frames, concatenated here:
/// <list type="bullet">
/// <item>bytes 0–6: hardware info (vendor, type, subtype, major, minor, storage size, protocol)</item>
/// <item>bytes 7–13: software info (same layout)</item>
/// <item>bytes 14–27: UID (7), batch number (5), production week (BCD), production year (BCD)</item>
/// </list>
/// The product is identified by the hardware type and major version together. The values follow
/// NXP's product data and the Proxmark3 client's <c>getCardType</c>.
/// </remarks>
internal static class DESFireVersionDecoder
{
    private const int FrameLength = 7;
    private const int ProductionLength = 14;
    private const byte VendorNxp = 0x04;

    /// <summary>
    /// Decodes as much of the GetVersion payload as is present. A card that stops after the
    /// hardware frame still yields its product, hardware version and storage size.
    /// </summary>
    public static DESFireDetails Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < FrameLength)
        {
            return VersionUnavailable("GetVersion response too short");
        }

        var hardware = payload[..FrameLength];
        var software = payload.Length >= 2 * FrameLength ? payload.Slice(FrameLength, FrameLength) : default;
        var production = payload.Length >= 2 * FrameLength + ProductionLength
            ? payload.Slice(2 * FrameLength, ProductionLength)
            : default;

        return new DESFireDetails
        {
            CardType = IdentifyProduct(hardware[0], hardware[1], hardware[3], hardware[4]),
            Vendor = hardware[0] == VendorNxp ? "NXP" : $"0x{hardware[0]:X2}",
            HardwareVersion = FormatVersion(hardware[3], hardware[4]),
            SoftwareVersion = software.IsEmpty ? null : FormatVersion(software[3], software[4]),
            StorageSize = FormatStorageSize(hardware[5]),
            BatchNumber = production.IsEmpty ? null : Convert.ToHexString(production.Slice(7, 5)),
            ProductionDate = production.IsEmpty ? null : FormatProductionDate(production[12], production[13]),
            VersionData = Convert.ToHexString(payload),
        };
    }

    /// <summary>
    /// Details for a card that is DESFire but whose version could not be read.
    /// </summary>
    public static DESFireDetails VersionUnavailable(string reason) => new()
    {
        CardType = "DESFire (version unknown)",
        VersionUnavailableReason = reason,
    };

    /// <summary>
    /// Names the product from the hardware vendor, type and version bytes.
    /// </summary>
    internal static string IdentifyProduct(byte vendor, byte type, byte major, byte minor)
    {
        if (vendor != VendorNxp)
        {
            return $"DESFire-compatible (vendor 0x{vendor:X2})";
        }

        return (type, major, minor) switch
        {
            (0x01, 0x00, _) => "DESFire (MF3ICD40)",
            (0x01, 0x01, _) => "DESFire EV1",
            (0x01, 0x12, _) => "DESFire EV2",
            (0x01, 0x22, _) => "DESFire EV2 XL",
            (0x01, 0x33, _) => "DESFire EV3",
            (0x01, 0xA0, _) => "MIFARE DUOX",
            (0x08, 0x30, _) => "DESFire Light",
            (0x81, 0x42, _) => "DESFire EV2 (JavaCard/SmartMX implementation)",
            (0x81, 0x43, _) => "DESFire EV3 (JavaCard/SmartMX implementation)",
            (0x91, 0x62, _) => "DESFire EV2 (Apple Wallet)",
            (0x02, 0x11, _) => "MIFARE Plus EV1",
            (0x02, 0x22, _) => "MIFARE Plus EV2",
            (0x04, 0x30, _) => "NTAG 424 DNA",
            // Proxmark3 matches NTAG 413 DNA on the version alone, whatever the type byte
            (_, 0x10, 0x00) => "NTAG 413 DNA",
            (0x01, _, _) => "DESFire (unrecognized version)",
            (0x02, _, _) => "MIFARE Plus (unrecognized version)",
            (0x04, _, _) => "NTAG (unrecognized version)",
            (0x08, _, _) => "DESFire Light (unrecognized version)",
            (0x81, _, _) => "DESFire on JavaCard/SmartMX (unrecognized version)",
            _ => $"Unrecognized NXP product (type 0x{type:X2})",
        };
    }

    /// <summary>
    /// Formats a major/minor pair in hex, the convention NXP and Proxmark3 use (EV2 = "12.0").
    /// </summary>
    internal static string FormatVersion(byte major, byte minor) => $"{major:X}.{minor:X}";

    /// <summary>
    /// Decodes the storage size byte. The upper 7 bits give n, for 2^n bytes. If the low bit is
    /// set, the size lies between 2^n and 2^(n+1) bytes.
    /// </summary>
    internal static string FormatStorageSize(byte value)
    {
        var exponent = value >> 1;
        if (exponent > 30)
        {
            return $"Unknown (0x{value:X2})";
        }

        var lower = 1L << exponent;
        return (value & 0x01) == 0
            ? FormatBytes(lower)
            : $"{FormatBytes(lower)} to {FormatBytes(lower << 1)}";
    }

    /// <summary>
    /// Formats the production week and year, which the card stores as BCD. Returns null when both
    /// bytes are zero (not recorded), and the raw bytes when they are not a valid BCD week and year.
    /// </summary>
    internal static string? FormatProductionDate(byte week, byte year)
    {
        if (week == 0 && year == 0)
        {
            return null;
        }

        if (TryDecodeBcd(week, out var weekNumber) && weekNumber is >= 1 and <= 53
            && TryDecodeBcd(year, out var yearNumber))
        {
            return $"Week {weekNumber}, {2000 + yearNumber}";
        }

        return $"Week 0x{week:X2}, year 0x{year:X2} (unrecognized format)";
    }

    private static bool TryDecodeBcd(byte value, out int result)
    {
        var high = value >> 4;
        var low = value & 0x0F;
        result = high * 10 + low;
        return high <= 9 && low <= 9;
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024 * 1024)} MB",
        >= 1024 => $"{bytes / 1024} KB",
        _ => $"{bytes} B",
    };
}
