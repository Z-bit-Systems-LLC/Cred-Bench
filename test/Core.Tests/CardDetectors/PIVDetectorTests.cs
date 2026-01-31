using CredBench.Core.Models;
using CredBench.Core.Services;
using CredBench.Core.Services.CardDetectors;
using Moq;
using NUnit.Framework;

namespace CredBench.Core.Tests.CardDetectors;

[TestFixture]
public class PIVDetectorTests
{
    private Mock<ICardConnection> _connectionMock = null!;
    private PIVDetector _detector = null!;

    [SetUp]
    public void Setup()
    {
        _connectionMock = new Mock<ICardConnection>();
        _detector = new PIVDetector();
    }

    [Test]
    public void Technology_ReturnsPIV()
    {
        Assert.That(_detector.Technology, Is.EqualTo(CardTechnology.PIV));
    }

    [Test]
    public void Detect_WithSuccessResponse_ReturnsDetected()
    {
        // Arrange
        byte[] successResponse = [0x90, 0x00];
        _connectionMock
            .Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Returns(successResponse);

        // Act
        var (detected, details) = _detector.Detect(_connectionMock.Object);

        // Assert
        Assert.That(detected, Is.True);
        Assert.That(details, Is.EqualTo("PIV application found"));
    }

    [Test]
    public void Detect_WithMoreDataResponse_ReturnsDetected()
    {
        // Arrange - SW 61xx indicates more data available
        byte[] moreDataResponse = [0x61, 0x10];
        _connectionMock
            .Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Returns(moreDataResponse);

        // Act
        var (detected, details) = _detector.Detect(_connectionMock.Object);

        // Assert
        Assert.That(detected, Is.True);
        Assert.That(details, Does.Contain("more data available"));
    }

    [Test]
    public void Detect_WithErrorResponse_ReturnsNotDetected()
    {
        // Arrange - SW 6A82 = File not found
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
    public void Detect_WithException_ReturnsNotDetectedWithError()
    {
        // Arrange
        _connectionMock
            .Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("Card removed"));

        // Act
        var (detected, details) = _detector.Detect(_connectionMock.Object);

        // Assert
        Assert.That(detected, Is.False);
        Assert.That(details, Does.Contain("error").IgnoreCase);
    }

    [Test]
    public void Detect_SendsCorrectSelectCommand()
    {
        // Arrange
        byte[]? capturedCommand = null;
        _connectionMock
            .Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Callback<byte[]>(cmd => capturedCommand = cmd)
            .Returns([0x90, 0x00]);

        // Act
        _detector.Detect(_connectionMock.Object);

        // Assert
        Assert.That(capturedCommand, Is.Not.Null);
        Assert.That(capturedCommand![0], Is.EqualTo(0x00)); // CLA
        Assert.That(capturedCommand[1], Is.EqualTo(0xA4)); // INS (SELECT)
        Assert.That(capturedCommand[2], Is.EqualTo(0x04)); // P1 (select by name)
        Assert.That(capturedCommand[3], Is.EqualTo(0x00)); // P2
        Assert.That(capturedCommand[4], Is.EqualTo(0x0B)); // Lc (AID length = 11)
        // PIV AID: A0 00 00 03 08 00 00 10 00 01 00
        Assert.That(capturedCommand[5], Is.EqualTo(0xA0));
        Assert.That(capturedCommand[6], Is.EqualTo(0x00));
        Assert.That(capturedCommand[7], Is.EqualTo(0x00));
        Assert.That(capturedCommand[8], Is.EqualTo(0x03));
        Assert.That(capturedCommand[9], Is.EqualTo(0x08));
    }
}
