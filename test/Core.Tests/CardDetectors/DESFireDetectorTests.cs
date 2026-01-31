using CredBench.Core.Models;
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
        var (detected, details) = _detector.Detect(_connectionMock.Object);

        // Assert
        Assert.That(detected, Is.True);
        Assert.That(details, Does.Contain("DESFire"));
    }

    [Test]
    public void Detect_WithMoreFramesResponse_ReturnsDetectedWithVersionInfo()
    {
        // Arrange - SELECT fails, GetVersion returns version info
        // Version info: vendor=04 (NXP), type=01, subtype=01, major=12 (EV2), minor=00, storage=1A (8KB), protocol=05
        byte[] versionResponse = [0x04, 0x01, 0x01, 0x12, 0x00, 0x1A, 0x05, 0x91, 0xAF];
        byte[] frame2Response = [0x04, 0x01, 0x01, 0x12, 0x00, 0x1A, 0x05, 0x91, 0xAF];
        byte[] frame3Response = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x91, 0x00];

        // SELECT commands fail
        _connectionMock
            .Setup(x => x.Transmit(It.Is<byte[]>(cmd => cmd[0] == 0x00 && cmd[1] == 0xA4)))
            .Returns(SelectFailResponse);

        // GetVersion commands succeed
        var getVersionCallCount = 0;
        _connectionMock
            .Setup(x => x.Transmit(It.Is<byte[]>(cmd => cmd[0] == 0x90)))
            .Returns(() =>
            {
                getVersionCallCount++;
                return getVersionCallCount switch
                {
                    1 => versionResponse,  // GetVersion response
                    2 => frame2Response,   // AdditionalFrame 1
                    3 => frame3Response,   // AdditionalFrame 2
                    _ => [0x91, 0x00]
                };
            });

        // Act
        var (detected, details) = _detector.Detect(_connectionMock.Object);

        // Assert
        Assert.That(detected, Is.True);
        Assert.That(details, Does.Contain("DESFire"));
        Assert.That(details, Does.Contain("EV2"));
        Assert.That(details, Does.Contain("8KB"));
    }

    [Test]
    public void Detect_WithErrorResponse_ReturnsNotDetected()
    {
        // Arrange - All commands fail
        _connectionMock
            .Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Returns<byte[]>(_ => [0x6A, 0x82]);

        // Act
        var (detected, details) = _detector.Detect(_connectionMock.Object);

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
        var (detected, details) = _detector.Detect(_connectionMock.Object);

        // Assert
        Assert.That(detected, Is.True);
        Assert.That(details, Does.Contain("DESFire"));
    }
}
