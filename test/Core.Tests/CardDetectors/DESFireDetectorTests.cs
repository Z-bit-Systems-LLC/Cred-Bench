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
        // Arrange - DESFire success: 91 00
        byte[] successResponse = [0x91, 0x00];
        _connectionMock
            .Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Returns(successResponse);

        // Act
        var (detected, details) = _detector.Detect(_connectionMock.Object);

        // Assert
        Assert.That(detected, Is.True);
        Assert.That(details, Does.Contain("DESFire"));
    }

    [Test]
    public void Detect_WithMoreFramesResponse_ReturnsDetectedWithVersionInfo()
    {
        // Arrange - DESFire response with version info (91 AF = more frames)
        // Version info: vendor=04 (NXP), type=01, subtype=01, major=12 (EV2), minor=00, storage=1A (8KB), protocol=05
        byte[] versionResponse = [0x04, 0x01, 0x01, 0x12, 0x00, 0x1A, 0x05, 0x91, 0xAF];
        byte[] frame2Response = [0x04, 0x01, 0x01, 0x12, 0x00, 0x1A, 0x05, 0x91, 0xAF];
        byte[] frame3Response = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x91, 0x00];

        var callCount = 0;
        _connectionMock
            .Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Returns(() =>
            {
                callCount++;
                return callCount switch
                {
                    1 => versionResponse,
                    2 => frame2Response,
                    3 => frame3Response,
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
        // Arrange - Non-DESFire error response
        byte[] errorResponse = [0x6A, 0x82];
        _connectionMock
            .Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Returns(errorResponse);

        // Act
        var (detected, details) = _detector.Detect(_connectionMock.Object);

        // Assert
        Assert.That(detected, Is.False);
        Assert.That(details, Is.Null);
    }

    [Test]
    public void Detect_SendsGetVersionCommand()
    {
        // Arrange
        byte[]? capturedCommand = null;
        _connectionMock
            .Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Callback<byte[]>(cmd => capturedCommand = cmd)
            .Returns([0x91, 0x00]);

        // Act
        _detector.Detect(_connectionMock.Object);

        // Assert
        Assert.That(capturedCommand, Is.Not.Null);
        Assert.That(capturedCommand![0], Is.EqualTo(0x90)); // CLA for DESFire wrapped
        Assert.That(capturedCommand[1], Is.EqualTo(0x60)); // INS (GetVersion)
        Assert.That(capturedCommand[2], Is.EqualTo(0x00)); // P1
        Assert.That(capturedCommand[3], Is.EqualTo(0x00)); // P2
        Assert.That(capturedCommand[4], Is.EqualTo(0x00)); // Le
    }
}
