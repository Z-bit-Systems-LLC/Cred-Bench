using CredBench.Core.Services.CardDetectors;
using NUnit.Framework;

namespace CredBench.Core.Tests.CardDetectors;

[TestFixture]
public class DESFireVersionDecoderTests
{
    // Hardware frame: vendor, type, subtype, major, minor, storage, protocol
    private static readonly byte[] Ev3Hardware = [0x04, 0x01, 0x01, 0x33, 0x00, 0x1A, 0x05];
    private static readonly byte[] Ev3Software = [0x04, 0x01, 0x01, 0x03, 0x00, 0x1A, 0x05];

    // Production frame: UID (7), batch (5), week (BCD), year (BCD)
    private static readonly byte[] Production =
        [0x04, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0xBA, 0x00, 0x12, 0x34, 0x56, 0x23, 0x21];

    [TestCase(0x01, 0x00, 0x02, "DESFire (MF3ICD40)")]
    [TestCase(0x01, 0x01, 0x00, "DESFire EV1")]
    [TestCase(0x01, 0x12, 0x00, "DESFire EV2")]
    [TestCase(0x01, 0x22, 0x00, "DESFire EV2 XL")]
    [TestCase(0x01, 0x33, 0x00, "DESFire EV3")]
    [TestCase(0x01, 0xA0, 0x00, "MIFARE DUOX")]
    [TestCase(0x08, 0x30, 0x00, "DESFire Light")]
    [TestCase(0x81, 0x42, 0x00, "DESFire EV2 (JavaCard/SmartMX implementation)")]
    [TestCase(0x81, 0x43, 0x01, "DESFire EV3 (JavaCard/SmartMX implementation)")]
    [TestCase(0x91, 0x62, 0x01, "DESFire EV2 (Apple Wallet)")]
    [TestCase(0x02, 0x11, 0x00, "MIFARE Plus EV1")]
    [TestCase(0x02, 0x22, 0x00, "MIFARE Plus EV2")]
    [TestCase(0x04, 0x30, 0x00, "NTAG 424 DNA")]
    [TestCase(0x04, 0x10, 0x00, "NTAG 413 DNA")]
    [TestCase(0x01, 0x55, 0x00, "DESFire (unrecognized version)")]
    [TestCase(0x81, 0x55, 0x00, "DESFire on JavaCard/SmartMX (unrecognized version)")]
    [TestCase(0x7F, 0x55, 0x00, "Unrecognized NXP product (type 0x7F)")]
    public void IdentifyProduct_MapsTypeAndMajorVersion(int type, int major, int minor, string expected)
    {
        var product = DESFireVersionDecoder.IdentifyProduct(0x04, (byte)type, (byte)major, (byte)minor);

        Assert.That(product, Is.EqualTo(expected));
    }

    [Test]
    public void IdentifyProduct_EV2XLAndPlusEV2ShareMajorVersion_TypeTellsThemApart()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DESFireVersionDecoder.IdentifyProduct(0x04, 0x01, 0x22, 0x00), Is.EqualTo("DESFire EV2 XL"));
            Assert.That(DESFireVersionDecoder.IdentifyProduct(0x04, 0x02, 0x22, 0x00), Is.EqualTo("MIFARE Plus EV2"));
        });
    }

    [Test]
    public void IdentifyProduct_NonNxpVendor_ReportsVendorByte()
    {
        var product = DESFireVersionDecoder.IdentifyProduct(0x05, 0x01, 0x33, 0x00);

        Assert.That(product, Is.EqualTo("DESFire-compatible (vendor 0x05)"));
    }

    [TestCase(0x01, 0x00, "1.0")]
    [TestCase(0x12, 0x00, "12.0")]
    [TestCase(0x33, 0x00, "33.0")]
    [TestCase(0x00, 0x02, "0.2")]
    [TestCase(0xA0, 0x00, "A0.0")]
    public void FormatVersion_UsesHex(int major, int minor, string expected)
    {
        Assert.That(DESFireVersionDecoder.FormatVersion((byte)major, (byte)minor), Is.EqualTo(expected));
    }

    [TestCase(0x16, "2 KB")]
    [TestCase(0x18, "4 KB")]
    [TestCase(0x1A, "8 KB")]
    [TestCase(0x1C, "16 KB")]
    [TestCase(0x1E, "32 KB")]
    [TestCase(0x13, "512 B to 1 KB")]
    [TestCase(0x11, "256 B to 512 B")]
    [TestCase(0x0F, "128 B to 256 B")]
    [TestCase(0xFF, "Unknown (0xFF)")]
    public void FormatStorageSize_DecodesPowerOfTwoAndRange(int value, string expected)
    {
        Assert.That(DESFireVersionDecoder.FormatStorageSize((byte)value), Is.EqualTo(expected));
    }

    [TestCase(0x23, 0x21, "Week 23, 2021")]
    [TestCase(0x01, 0x09, "Week 1, 2009")]
    [TestCase(0x53, 0x26, "Week 53, 2026")]
    [TestCase(0x1A, 0x21, "Week 0x1A, year 0x21 (unrecognized format)")]
    [TestCase(0x60, 0x21, "Week 0x60, year 0x21 (unrecognized format)")]
    [TestCase(0x23, 0x2F, "Week 0x23, year 0x2F (unrecognized format)")]
    public void FormatProductionDate_DecodesBcd(int week, int year, string expected)
    {
        Assert.That(DESFireVersionDecoder.FormatProductionDate((byte)week, (byte)year), Is.EqualTo(expected));
    }

    [Test]
    public void FormatProductionDate_AllZero_ReturnsNull()
    {
        Assert.That(DESFireVersionDecoder.FormatProductionDate(0x00, 0x00), Is.Null);
    }

    [Test]
    public void Decode_FullPayload_FillsEveryField()
    {
        byte[] payload = [.. Ev3Hardware, .. Ev3Software, .. Production];

        var details = DESFireVersionDecoder.Decode(payload);

        Assert.Multiple(() =>
        {
            Assert.That(details.CardType, Is.EqualTo("DESFire EV3"));
            Assert.That(details.Vendor, Is.EqualTo("NXP"));
            Assert.That(details.HardwareVersion, Is.EqualTo("33.0"));
            Assert.That(details.SoftwareVersion, Is.EqualTo("3.0"));
            Assert.That(details.StorageSize, Is.EqualTo("8 KB"));
            Assert.That(details.BatchNumber, Is.EqualTo("BA00123456"));
            Assert.That(details.ProductionDate, Is.EqualTo("Week 23, 2021"));
            Assert.That(details.VersionData, Is.EqualTo(Convert.ToHexString(payload)));
            Assert.That(details.VersionUnavailableReason, Is.Null);
        });
    }

    [Test]
    public void Decode_HardwareFrameOnly_LeavesLaterFieldsNull()
    {
        var details = DESFireVersionDecoder.Decode(Ev3Hardware);

        Assert.Multiple(() =>
        {
            Assert.That(details.CardType, Is.EqualTo("DESFire EV3"));
            Assert.That(details.HardwareVersion, Is.EqualTo("33.0"));
            Assert.That(details.StorageSize, Is.EqualTo("8 KB"));
            Assert.That(details.SoftwareVersion, Is.Null);
            Assert.That(details.BatchNumber, Is.Null);
            Assert.That(details.ProductionDate, Is.Null);
        });
    }

    [Test]
    public void Decode_HardwareAndSoftwareOnly_HasSoftwareVersionButNoProductionData()
    {
        byte[] payload = [.. Ev3Hardware, .. Ev3Software];

        var details = DESFireVersionDecoder.Decode(payload);

        Assert.Multiple(() =>
        {
            Assert.That(details.SoftwareVersion, Is.EqualTo("3.0"));
            Assert.That(details.BatchNumber, Is.Null);
            Assert.That(details.ProductionDate, Is.Null);
        });
    }

    [Test]
    public void Decode_LongerThirdFrame_IgnoresExtraBytes()
    {
        // EV2 and later can append a FabKey identifier to the third frame
        byte[] payload = [.. Ev3Hardware, .. Ev3Software, .. Production, 0x1F];

        var details = DESFireVersionDecoder.Decode(payload);

        Assert.That(details.ProductionDate, Is.EqualTo("Week 23, 2021"));
    }

    [Test]
    public void Decode_DesfireLight_ReportsStorageRange()
    {
        byte[] hardware = [0x04, 0x08, 0x01, 0x30, 0x00, 0x13, 0x05];

        var details = DESFireVersionDecoder.Decode(hardware);

        Assert.Multiple(() =>
        {
            Assert.That(details.CardType, Is.EqualTo("DESFire Light"));
            Assert.That(details.StorageSize, Is.EqualTo("512 B to 1 KB"));
        });
    }

    [Test]
    public void Decode_WavelynxDuoxDump_IdentifiesDuox()
    {
        // Hardware and software frames read from a production Wavelynx DUOX (docs/LEAF.md, 2026-11-05)
        byte[] payload =
        [
            0x04, 0x01, 0x01, 0xA0, 0x00, 0x18, 0x25,
            0x04, 0x01, 0x07, 0x00, 0x01, 0x18, 0x25,
        ];

        var details = DESFireVersionDecoder.Decode(payload);

        Assert.Multiple(() =>
        {
            Assert.That(details.CardType, Is.EqualTo("MIFARE DUOX"));
            Assert.That(details.HardwareVersion, Is.EqualTo("A0.0"));
            Assert.That(details.SoftwareVersion, Is.EqualTo("0.1"));
            Assert.That(details.StorageSize, Is.EqualTo("4 KB"));
        });
    }

    [Test]
    public void Decode_TooShort_ReportsVersionUnavailable()
    {
        var details = DESFireVersionDecoder.Decode([0x04, 0x01, 0x01]);

        Assert.Multiple(() =>
        {
            Assert.That(details.CardType, Is.EqualTo("DESFire (version unknown)"));
            Assert.That(details.HardwareVersion, Is.Null);
            Assert.That(details.VersionUnavailableReason, Is.Not.Null);
        });
    }
}
