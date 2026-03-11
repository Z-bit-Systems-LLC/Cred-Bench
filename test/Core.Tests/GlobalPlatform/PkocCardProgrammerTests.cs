using CredBench.Core.Services;
using CredBench.Core.Services.Pkoc;
using Moq;
using NUnit.Framework;

namespace CredBench.Core.Tests.GlobalPlatform;

[TestFixture]
public class PkocCardProgrammerTests
{
    [Test]
    public void IsAppletInstalled_SelectSuccess_ReturnsTrue()
    {
        var mock = new Mock<ICardConnection>();
        mock.Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Returns([0x5C, 0x02, 0x01, 0x01, 0x90, 0x00]);

        var programmer = new PkocCardProgrammer();
        Assert.That(programmer.IsAppletInstalled(mock.Object), Is.True);
    }

    [Test]
    public void IsAppletInstalled_SelectFails_ReturnsFalse()
    {
        var mock = new Mock<ICardConnection>();
        mock.Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Returns([0x6A, 0x82]); // File not found

        var programmer = new PkocCardProgrammer();
        Assert.That(programmer.IsAppletInstalled(mock.Object), Is.False);
    }

    [Test]
    public void IsAppletInstalled_SendsCorrectSelectCommand()
    {
        var mock = new Mock<ICardConnection>();
        byte[]? command = null;

        mock.Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Callback<byte[]>(cmd => command = cmd)
            .Returns([0x90, 0x00]);

        new PkocCardProgrammer().IsAppletInstalled(mock.Object);

        Assert.That(command, Is.Not.Null);
        Assert.That(command![0], Is.EqualTo(0x00)); // CLA
        Assert.That(command[1], Is.EqualTo(0xA4)); // INS SELECT
        Assert.That(command[2], Is.EqualTo(0x04)); // P1 by name
        // PKOC AID: A000000898000001
        Assert.That(command[5], Is.EqualTo(0xA0));
        Assert.That(command[12], Is.EqualTo(0x01));
    }

    [Test]
    public void DefaultGpKey_Is16Bytes()
    {
        Assert.That(PkocCardProgrammer.DefaultGpKey, Has.Length.EqualTo(16));
    }

    [Test]
    public void DefaultGpKey_MatchesStandardGpDefault()
    {
        // Standard GP default: 404142434445464748494A4B4C4D4E4F
        Assert.That(PkocCardProgrammer.DefaultGpKey[0], Is.EqualTo(0x40));
        Assert.That(PkocCardProgrammer.DefaultGpKey[15], Is.EqualTo(0x4F));
    }
}
