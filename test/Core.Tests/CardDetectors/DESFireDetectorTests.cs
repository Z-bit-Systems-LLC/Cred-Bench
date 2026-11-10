using CredBench.Core.Models;
using CredBench.Core.Models.TechnologyDetails;
using CredBench.Core.Services;
using CredBench.Core.Services.CardDetectors;
using Moq;
using NUnit.Framework;

namespace CredBench.Core.Tests.CardDetectors;

[TestFixture]
public class DESFireDetectorTests
{
    private Mock<ICardConnection> _connectionMock = null!;
    private DESFireDetector _detector = null!;

    // SELECT failure response
    private static readonly byte[] SelectFailResponse = [0x6A, 0x82];

    // GetVersion native wrapped command
    private static readonly byte[] GetVersionCommand = [0x90, 0x60, 0x00, 0x00, 0x00];

    [SetUp]
    public void Setup()
    {
        _connectionMock = new Mock<ICardConnection>();
        _detector = new DESFireDetector();
    }

    [Test]
    public void Technology_ReturnsDESFire()
    {
        Assert.That(_detector.Technology, Is.EqualTo(CardTechnology.DESFire));
    }

    [Test]
    public void Detect_WithSuccessResponse_ReturnsDetected()
    {
        // Arrange - SELECT fails, GetVersion succeeds with 91 00
        _connectionMock
            .Setup(x => x.Transmit(It.Is<byte[]>(cmd => cmd[0] == 0x00 && cmd[1] == 0xA4)))
            .Returns(SelectFailResponse);

        _connectionMock
            .Setup(x => x.Transmit(It.Is<byte[]>(cmd => cmd[0] == 0x90 && cmd[1] == 0x60)))
            .Returns<byte[]>(_ => [0x91, 0x00]);

        // Act
        var (detected, details, _) = _detector.Detect(_connectionMock.Object);

        // Assert
        Assert.That(detected, Is.True);
        Assert.That(details, Does.Contain("DESFire"));
    }

    // GetVersion frames for an EV2 8 KB card
    // Hardware: vendor=04 (NXP), type=01, subtype=01, major=12 (EV2), minor=00, storage=1A (8 KB), protocol=05
    private static readonly byte[] Ev2HardwareFrame = [0x04, 0x01, 0x01, 0x12, 0x00, 0x1A, 0x05, 0x91, 0xAF];
    // Software: major=02, minor=01
    private static readonly byte[] Ev2SoftwareFrame = [0x04, 0x01, 0x01, 0x02, 0x01, 0x1A, 0x05, 0x91, 0xAF];
    // Production: UID (7), batch (5), week 23 (BCD), year 2021 (BCD)
    private static readonly byte[] Ev2ProductionFrame =
        [0x04, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0xBA, 0x00, 0x12, 0x34, 0x56, 0x23, 0x21, 0x91, 0x00];

    private void SetupSelectFails() =>
        _connectionMock
            .Setup(x => x.Transmit(It.Is<byte[]>(cmd => cmd[0] == 0x00 && cmd[1] == 0xA4)))
            .Returns(SelectFailResponse);

    /// <summary>
    /// Answers native-wrapped commands (CLA 0x90) with the given frames in order.
    /// </summary>
    private void SetupNativeFrames(params Func<byte[]>[] frames)
    {
        var call = 0;
        _connectionMock
            .Setup(x => x.Transmit(It.Is<byte[]>(cmd => cmd[0] == 0x90)))
            .Returns(() => call < frames.Length ? frames[call++]() : [0x91, 0x00]);
    }

    [Test]
    public void Detect_WithMoreFramesResponse_ReturnsDetectedWithVersionInfo()
    {
        SetupSelectFails();
        SetupNativeFrames(() => Ev2HardwareFrame, () => Ev2SoftwareFrame, () => Ev2ProductionFrame);

        // Act
        var (detected, details, typed) = _detector.Detect(_connectionMock.Object);

        // Assert
        Assert.That(detected, Is.True);
        Assert.That(details, Is.EqualTo("DESFire EV2, 8 KB storage"));

        var desfire = typed as DESFireDetails;
        Assert.That(desfire, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(desfire!.CardType, Is.EqualTo("DESFire EV2"));
            Assert.That(desfire.HardwareVersion, Is.EqualTo("12.0"));
            Assert.That(desfire.SoftwareVersion, Is.EqualTo("2.1"));
            Assert.That(desfire.StorageSize, Is.EqualTo("8 KB"));
            Assert.That(desfire.BatchNumber, Is.EqualTo("BA00123456"));
            Assert.That(desfire.ProductionDate, Is.EqualTo("Week 23, 2021"));
            Assert.That(desfire.VersionData, Is.EqualTo(
                "04010112001A05" + "04010102011A05" + "04112233445566" + "BA00123456" + "2321"));
        });
    }

    [Test]
    public void Detect_WhenAdditionalFrameThrows_KeepsHardwareInfo()
    {
        SetupSelectFails();
        SetupNativeFrames(() => Ev2HardwareFrame, () => throw new InvalidOperationException("card removed"));

        var (detected, _, typed) = _detector.Detect(_connectionMock.Object);

        var desfire = typed as DESFireDetails;
        Assert.That(detected, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(desfire!.CardType, Is.EqualTo("DESFire EV2"));
            Assert.That(desfire.HardwareVersion, Is.EqualTo("12.0"));
            Assert.That(desfire.SoftwareVersion, Is.Null);
            Assert.That(desfire.ProductionDate, Is.Null);
        });
    }

    [Test]
    public void Detect_WhenThirdFrameFails_KeepsHardwareAndSoftwareInfo()
    {
        SetupSelectFails();
        SetupNativeFrames(() => Ev2HardwareFrame, () => Ev2SoftwareFrame, () => [0x91, 0xCA]);

        var (_, _, typed) = _detector.Detect(_connectionMock.Object);

        var desfire = typed as DESFireDetails;
        Assert.Multiple(() =>
        {
            Assert.That(desfire!.SoftwareVersion, Is.EqualTo("2.1"));
            Assert.That(desfire.ProductionDate, Is.Null);
            Assert.That(desfire.BatchNumber, Is.Null);
        });
    }

    [Test]
    public void Detect_StopsAfterThreeFrames()
    {
        SetupSelectFails();
        // A misbehaving card that keeps saying "more frames"
        SetupNativeFrames(() => Ev2HardwareFrame, () => Ev2SoftwareFrame, () => Ev2SoftwareFrame, () => Ev2SoftwareFrame);

        _detector.Detect(_connectionMock.Object);

        // GetVersion + two AdditionalFrame
        _connectionMock.Verify(x => x.Transmit(It.Is<byte[]>(cmd => cmd[0] == 0x90)), Times.Exactly(3));
    }

    [Test]
    public void Detect_WithGetVersionRejected_ReportsVersionUnavailable()
    {
        SetupSelectFails();
        SetupNativeFrames(() => [0x91, 0x1C]);

        var (detected, details, typed) = _detector.Detect(_connectionMock.Object);

        var desfire = typed as DESFireDetails;
        Assert.That(detected, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(details, Does.Contain("version unknown"));
            Assert.That(desfire!.CardType, Is.EqualTo("DESFire (version unknown)"));
            Assert.That(desfire.HardwareVersion, Is.Null);
            Assert.That(desfire.VersionUnavailableReason, Is.EqualTo("GetVersion rejected: illegal command"));
        });
    }

    [Test]
    public void Detect_WithErrorResponse_ReturnsNotDetected()
    {
        // Arrange - All commands fail
        _connectionMock
            .Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Returns<byte[]>(_ => [0x6A, 0x82]);

        // Act
        var (detected, details, _) = _detector.Detect(_connectionMock.Object);

        // Assert
        Assert.That(detected, Is.False);
        Assert.That(details, Is.Null);
    }

    [Test]
    public void Detect_WithSelectSuccess_ReturnsDetected()
    {
        // Arrange - SELECT succeeds, GetVersion fails
        _connectionMock
            .Setup(x => x.Transmit(It.Is<byte[]>(cmd => cmd[0] == 0x00 && cmd[1] == 0xA4)))
            .Returns<byte[]>(_ => [0x90, 0x00]);

        _connectionMock
            .Setup(x => x.Transmit(It.Is<byte[]>(cmd => cmd[0] == 0x90)))
            .Returns<byte[]>(_ => [0x6E, 0x00]);

        // Act
        var (detected, details, typed) = _detector.Detect(_connectionMock.Object);

        // Assert
        Assert.That(detected, Is.True);
        Assert.That(details, Does.Contain("DESFire"));
        Assert.That(typed, Is.TypeOf<DESFireDetails>()
            .With.Property(nameof(DESFireDetails.VersionUnavailableReason))
            .EqualTo("GetVersion not answered after SELECT"));
    }
}
