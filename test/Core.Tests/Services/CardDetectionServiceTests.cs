using CredBench.Core.Models;
using CredBench.Core.Services;
using CredBench.Core.Services.CardDetectors;
using Moq;
using NUnit.Framework;

namespace CredBench.Core.Tests.Services;

[TestFixture]
public class CardDetectionServiceTests
{
    private Mock<ISmartCardService> _smartCardServiceMock = null!;
    private Mock<ICardConnection> _connectionMock = null!;

    [SetUp]
    public void Setup()
    {
        _connectionMock = new Mock<ICardConnection>();
        _connectionMock.Setup(x => x.GetATR()).Returns("3B 8F 80 01");
        _connectionMock.Setup(x => x.GetUID()).Returns("04 A2 B3 C4");

        _smartCardServiceMock = new Mock<ISmartCardService>();
        _smartCardServiceMock.Setup(x => x.Connect(It.IsAny<string>())).Returns(_connectionMock.Object);
    }

    [Test]
    public async Task DetectAsync_WithNoDetectors_ReturnsUnknown()
    {
        // Arrange
        var service = new CardDetectionService(_smartCardServiceMock.Object, []);

        // Act
        var result = await service.DetectAsync("TestReader");

        // Assert
        Assert.That(result.Technologies, Is.EqualTo(CardTechnology.Unknown));
        Assert.That(result.ATR, Is.EqualTo("3B 8F 80 01"));
        Assert.That(result.UID, Is.EqualTo("04 A2 B3 C4"));
    }

    [Test]
    public async Task DetectAsync_WithSingleDetector_ReturnsDetectedTechnology()
    {
        // Arrange
        var detectorMock = new Mock<ICardDetector>();
        detectorMock.Setup(x => x.Technology).Returns(CardTechnology.PIV);
        detectorMock
            .Setup(x => x.Detect(It.IsAny<ICardConnection>()))
            .Returns((true, "PIV detected"));

        var service = new CardDetectionService(_smartCardServiceMock.Object, [detectorMock.Object]);

        // Act
        var result = await service.DetectAsync("TestReader");

        // Assert
        Assert.That(result.Technologies, Is.EqualTo(CardTechnology.PIV));
        Assert.That(result.HasTechnology(CardTechnology.PIV), Is.True);
        Assert.That(result.Details, Has.Count.EqualTo(1));
        Assert.That(result.Details[CardTechnology.PIV], Is.EqualTo("PIV detected"));
    }

    [Test]
    public async Task DetectAsync_WithMultipleDetectors_ReturnsCombinedTechnologies()
    {
        // Arrange
        var pivDetectorMock = new Mock<ICardDetector>();
        pivDetectorMock.Setup(x => x.Technology).Returns(CardTechnology.PIV);
        pivDetectorMock
            .Setup(x => x.Detect(It.IsAny<ICardConnection>()))
            .Returns((true, "PIV found"));

        var desfireDetectorMock = new Mock<ICardDetector>();
        desfireDetectorMock.Setup(x => x.Technology).Returns(CardTechnology.DESFire);
        desfireDetectorMock
            .Setup(x => x.Detect(It.IsAny<ICardConnection>()))
            .Returns((true, "DESFire EV2"));

        var service = new CardDetectionService(
            _smartCardServiceMock.Object,
            [pivDetectorMock.Object, desfireDetectorMock.Object]);

        // Act
        var result = await service.DetectAsync("TestReader");

        // Assert
        Assert.That(result.HasTechnology(CardTechnology.PIV), Is.True);
        Assert.That(result.HasTechnology(CardTechnology.DESFire), Is.True);
        Assert.That(result.Technologies, Is.EqualTo(CardTechnology.PIV | CardTechnology.DESFire));
        Assert.That(result.Details, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task DetectAsync_WithFailingDetector_ContinuesWithOthers()
    {
        // Arrange
        var failingDetectorMock = new Mock<ICardDetector>();
        failingDetectorMock.Setup(x => x.Technology).Returns(CardTechnology.IClass);
        failingDetectorMock
            .Setup(x => x.Detect(It.IsAny<ICardConnection>()))
            .Throws(new InvalidOperationException("Detector error"));

        var workingDetectorMock = new Mock<ICardDetector>();
        workingDetectorMock.Setup(x => x.Technology).Returns(CardTechnology.PIV);
        workingDetectorMock
            .Setup(x => x.Detect(It.IsAny<ICardConnection>()))
            .Returns((true, "PIV found"));

        var service = new CardDetectionService(
            _smartCardServiceMock.Object,
            [failingDetectorMock.Object, workingDetectorMock.Object]);

        // Act
        var result = await service.DetectAsync("TestReader");

        // Assert
        Assert.That(result.HasTechnology(CardTechnology.PIV), Is.True);
        Assert.That(result.HasTechnology(CardTechnology.IClass), Is.False);
    }

    [Test]
    public async Task DetectAsync_WithNotDetectedResults_ReturnsUnknown()
    {
        // Arrange
        var detectorMock = new Mock<ICardDetector>();
        detectorMock.Setup(x => x.Technology).Returns(CardTechnology.PIV);
        detectorMock
            .Setup(x => x.Detect(It.IsAny<ICardConnection>()))
            .Returns((false, null));

        var service = new CardDetectionService(_smartCardServiceMock.Object, [detectorMock.Object]);

        // Act
        var result = await service.DetectAsync("TestReader");

        // Assert
        Assert.That(result.Technologies, Is.EqualTo(CardTechnology.Unknown));
        Assert.That(result.Details, Is.Empty);
    }

    [Test]
    public void DetectAsync_Cancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var detectorMock = new Mock<ICardDetector>();
        detectorMock.Setup(x => x.Technology).Returns(CardTechnology.PIV);
        detectorMock
            .Setup(x => x.Detect(It.IsAny<ICardConnection>()))
            .Throws(new OperationCanceledException());

        var service = new CardDetectionService(_smartCardServiceMock.Object, [detectorMock.Object]);

        // Act & Assert - TaskCanceledException inherits from OperationCanceledException
        Assert.That(
            async () => await service.DetectAsync("TestReader", cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }
}
