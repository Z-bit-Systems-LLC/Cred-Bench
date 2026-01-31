using CredBench.Core.Models;
using NUnit.Framework;

namespace CredBench.Core.Tests.Models;

[TestFixture]
public class DetectionResultTests
{
    [Test]
    public void HasTechnology_WithSingleTechnology_ReturnsCorrectly()
    {
        // Arrange
        var result = new DetectionResult { Technologies = CardTechnology.PIV };

        // Assert
        Assert.That(result.HasTechnology(CardTechnology.PIV), Is.True);
        Assert.That(result.HasTechnology(CardTechnology.DESFire), Is.False);
        Assert.That(result.HasTechnology(CardTechnology.IClass), Is.False);
        Assert.That(result.HasTechnology(CardTechnology.PKOC), Is.False);
    }

    [Test]
    public void HasTechnology_WithMultipleTechnologies_ReturnsCorrectly()
    {
        // Arrange
        var result = new DetectionResult
        {
            Technologies = CardTechnology.PIV | CardTechnology.DESFire
        };

        // Assert
        Assert.That(result.HasTechnology(CardTechnology.PIV), Is.True);
        Assert.That(result.HasTechnology(CardTechnology.DESFire), Is.True);
        Assert.That(result.HasTechnology(CardTechnology.IClass), Is.False);
        Assert.That(result.HasTechnology(CardTechnology.PKOC), Is.False);
    }

    [Test]
    public void HasTechnology_WithUnknown_ReturnsFalseForAll()
    {
        // Arrange
        var result = new DetectionResult { Technologies = CardTechnology.Unknown };

        // Assert
        Assert.That(result.HasTechnology(CardTechnology.PIV), Is.False);
        Assert.That(result.HasTechnology(CardTechnology.DESFire), Is.False);
        Assert.That(result.HasTechnology(CardTechnology.IClass), Is.False);
        Assert.That(result.HasTechnology(CardTechnology.PKOC), Is.False);
    }

    [Test]
    public void DefaultValues_AreCorrect()
    {
        // Arrange
        var result = new DetectionResult();

        // Assert
        Assert.That(result.Technologies, Is.EqualTo(CardTechnology.Unknown));
        Assert.That(result.ATR, Is.Null);
        Assert.That(result.UID, Is.Null);
        Assert.That(result.DetectedAIDs, Is.Empty);
        Assert.That(result.Details, Is.Empty);
    }

    [Test]
    public void Record_WithAllProperties_SetsCorrectly()
    {
        // Arrange
        var details = new Dictionary<CardTechnology, string>
        {
            [CardTechnology.PIV] = "PIV found"
        };

        var result = new DetectionResult
        {
            Technologies = CardTechnology.PIV,
            ATR = "3B 8F 80 01",
            UID = "04 A2 B3 C4",
            DetectedAIDs = ["A0000003080000100001"],
            Details = details
        };

        // Assert
        Assert.That(result.Technologies, Is.EqualTo(CardTechnology.PIV));
        Assert.That(result.ATR, Is.EqualTo("3B 8F 80 01"));
        Assert.That(result.UID, Is.EqualTo("04 A2 B3 C4"));
        Assert.That(result.DetectedAIDs, Has.Count.EqualTo(1));
        Assert.That(result.Details[CardTechnology.PIV], Is.EqualTo("PIV found"));
    }
}
