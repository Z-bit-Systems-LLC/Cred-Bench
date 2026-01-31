using CredBench.Core.Models;
using CredBench.Core.Services;
using CredBench.Core.Services.CardDetectors;
using Moq;
using NUnit.Framework;

namespace CredBench.Core.Tests.CardDetectors;

[TestFixture]
public class LEAFDetectorTests
{
    private Mock<ICardConnection> _connectionMock = null!;
    private LEAFDetector _detector = null!;

    // LEAF ISO SELECT commands (00 A4 04 00 03 [AID] 00)
    private static readonly byte[] SelectUniversalId1 = [0x00, 0xA4, 0x04, 0x00, 0x03, 0xF5, 0x1C, 0xD8, 0x00]; // F51CD8
    private static readonly byte[] SelectUniversalId2 = [0x00, 0xA4, 0x04, 0x00, 0x03, 0xF5, 0x1C, 0xD9, 0x00]; // F51CD9
    private static readonly byte[] SelectEnterpriseId = [0x00, 0xA4, 0x04, 0x00, 0x03, 0xF5, 0x1C, 0xDB, 0x00]; // F51CDB

    [SetUp]
    public void Setup()
    {
        _connectionMock = new Mock<ICardConnection>();
        _detector = new LEAFDetector();
    }

    [Test]
    public void Technology_ReturnsLEAF()
    {
        Assert.That(_detector.Technology, Is.EqualTo(CardTechnology.LEAF));
    }

    [Test]
    public void Detect_WithUniversalId1Success_ReturnsDetected()
    {
        // Arrange - ISO success: 90 00
        _connectionMock
            .Setup(x => x.Transmit(It.Is<byte[]>(cmd => cmd.SequenceEqual(SelectUniversalId1))))
            .Returns([0x90, 0x00]);

        // Act
        var (detected, details, _) = _detector.Detect(_connectionMock.Object);

        // Assert
        Assert.That(detected, Is.True);
        Assert.That(details, Does.Contain("UNIVERSAL ID"));
    }

    [Test]
    public void Detect_WithUniversalId2Success_ReturnsDetected()
    {
        // Arrange - First AID fails, second succeeds
        _connectionMock
            .Setup(x => x.Transmit(It.Is<byte[]>(cmd => cmd.SequenceEqual(SelectUniversalId1))))
            .Returns([0x6A, 0x82]); // File not found
        _connectionMock
            .Setup(x => x.Transmit(It.Is<byte[]>(cmd => cmd.SequenceEqual(SelectUniversalId2))))
            .Returns([0x90, 0x00]);

        // Act
        var (detected, details, _) = _detector.Detect(_connectionMock.Object);

        // Assert
        Assert.That(detected, Is.True);
        Assert.That(details, Does.Contain("UNIVERSAL ID"));
    }

    [Test]
    public void Detect_WithEnterpriseIdSuccess_ReturnsDetected()
    {
        // Arrange - Universal ID AIDs fail, Enterprise ID succeeds
        _connectionMock
            .Setup(x => x.Transmit(It.Is<byte[]>(cmd => cmd.SequenceEqual(SelectUniversalId1))))
            .Returns([0x6A, 0x82]); // File not found
        _connectionMock
            .Setup(x => x.Transmit(It.Is<byte[]>(cmd => cmd.SequenceEqual(SelectUniversalId2))))
            .Returns([0x6A, 0x82]); // File not found
        _connectionMock
            .Setup(x => x.Transmit(It.Is<byte[]>(cmd => cmd.SequenceEqual(SelectEnterpriseId))))
            .Returns([0x90, 0x00]);

        // Act
        var (detected, details, _) = _detector.Detect(_connectionMock.Object);

        // Assert
        Assert.That(detected, Is.True);
        Assert.That(details, Does.Contain("ENTERPRISE ID"));
    }

    [Test]
    public void Detect_WithMoreDataResponse_ReturnsDetected()
    {
        // Arrange - More data available: 61 xx
        _connectionMock
            .Setup(x => x.Transmit(It.Is<byte[]>(cmd => cmd.SequenceEqual(SelectUniversalId1))))
            .Returns([0x61, 0x20]);

        // Act
        var (detected, details, _) = _detector.Detect(_connectionMock.Object);

        // Assert
        Assert.That(detected, Is.True);
        Assert.That(details, Does.Contain("LEAF"));
    }

    [Test]
    public void Detect_WithAllApplicationsNotFound_ReturnsNotDetected()
    {
        // Arrange - All AIDs return file not found (6A 82)
        _connectionMock
            .Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Returns([0x6A, 0x82]);

        // Act
        var (detected, details, _) = _detector.Detect(_connectionMock.Object);

        // Assert
        Assert.That(detected, Is.False);
        Assert.That(details, Is.Null);
    }

    [Test]
    public void Detect_WithNonDesfireCard_ReturnsNotDetected()
    {
        // Arrange - Non-DESFire response: 6A 82 (file not found)
        _connectionMock
            .Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Returns([0x6A, 0x82]);

        // Act
        var (detected, details, _) = _detector.Detect(_connectionMock.Object);

        // Assert
        Assert.That(detected, Is.False);
        Assert.That(details, Is.Null);
    }

    [Test]
    public void Detect_WithException_ReturnsNotDetected()
    {
        // Arrange
        _connectionMock
            .Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("Card removed"));

        // Act
        var (detected, details, _) = _detector.Detect(_connectionMock.Object);

        // Assert
        Assert.That(detected, Is.False);
        Assert.That(details, Is.Null);
    }

    [Test]
    public void Detect_SendsCorrectSelectCommand()
    {
        // Arrange
        byte[]? firstCommand = null;
        _connectionMock
            .Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Callback<byte[]>(cmd => firstCommand ??= cmd) // Only capture the first command
            .Returns([0x90, 0x00]);

        // Act
        _detector.Detect(_connectionMock.Object);

        // Assert - First command should be ISO SELECT for F51CD8
        Assert.That(firstCommand, Is.Not.Null);
        Assert.That(firstCommand![0], Is.EqualTo(0x00)); // CLA (ISO)
        Assert.That(firstCommand[1], Is.EqualTo(0xA4)); // INS (SELECT)
        Assert.That(firstCommand[2], Is.EqualTo(0x04)); // P1 (Select by DF name)
        Assert.That(firstCommand[3], Is.EqualTo(0x00)); // P2
        Assert.That(firstCommand[4], Is.EqualTo(0x03)); // Lc (AID length = 3)
        // AID F51CD8 in big-endian: F5 1C D8
        Assert.That(firstCommand[5], Is.EqualTo(0xF5));
        Assert.That(firstCommand[6], Is.EqualTo(0x1C));
        Assert.That(firstCommand[7], Is.EqualTo(0xD8));
        Assert.That(firstCommand[8], Is.EqualTo(0x00)); // Le
    }
}
