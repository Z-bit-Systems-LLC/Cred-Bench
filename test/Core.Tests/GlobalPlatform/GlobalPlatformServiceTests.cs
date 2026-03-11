using CredBench.Core.Services;
using CredBench.Core.Services.GlobalPlatform;
using Moq;
using NUnit.Framework;

namespace CredBench.Core.Tests.GlobalPlatform;

[TestFixture]
public class GlobalPlatformServiceTests
{
    private static readonly byte[] TestKey =
    [
        0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47,
        0x48, 0x49, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F
    ];

    [Test]
    public void EstablishSecureChannel_SendsSelectIsdFirst()
    {
        var mock = new Mock<ICardConnection>();
        byte[]? firstCommand = null;

        mock.Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Callback<byte[]>(cmd => firstCommand ??= cmd)
            .Returns([0x90, 0x00]);

        // Will fail at INITIALIZE UPDATE parsing, but we can check the first command
        try { new GlobalPlatformService().EstablishSecureChannel(mock.Object, TestKey); }
        catch { /* expected — INIT UPDATE response too short */ }

        Assert.That(firstCommand, Is.Not.Null);
        Assert.That(firstCommand![1], Is.EqualTo(0xA4), "First command should be SELECT");
        Assert.That(firstCommand[2], Is.EqualTo(0x04), "P1 should be 'by name'");
    }

    [Test]
    public void EstablishSecureChannel_SendsInitializeUpdateSecond()
    {
        var mock = new Mock<ICardConnection>();
        var commands = new List<byte[]>();

        mock.Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Callback<byte[]>(cmd => commands.Add((byte[])cmd.Clone()))
            .Returns([0x90, 0x00]);

        try { new GlobalPlatformService().EstablishSecureChannel(mock.Object, TestKey); }
        catch { /* expected */ }

        Assert.That(commands.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(commands[1][0], Is.EqualTo(0x80), "CLA should be 0x80");
        Assert.That(commands[1][1], Is.EqualTo(0x50), "INS should be INITIALIZE UPDATE");
        Assert.That(commands[1][4], Is.EqualTo(0x08), "Lc should be 8 (host challenge)");
    }

    [Test]
    public void EstablishSecureChannel_RejectsNonScp02()
    {
        var mock = new Mock<ICardConnection>();
        var callCount = 0;

        mock.Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1) return [0x90, 0x00]; // SELECT OK

                // INITIALIZE UPDATE response with SCP version 0x03 (not 02)
                var resp = new byte[30]; // 28 data + 2 SW
                resp[11] = 0x03; // key_info[1] = SCP03 (unsupported)
                resp[^2] = 0x90;
                resp[^1] = 0x00;
                return resp;
            });

        var ex = Assert.Throws<GlobalPlatformException>(() =>
            new GlobalPlatformService().EstablishSecureChannel(mock.Object, TestKey));

        Assert.That(ex!.Message, Does.Contain("SCP version"));
    }

    [Test]
    public void EstablishSecureChannel_RejectsBadCardCryptogram()
    {
        var mock = new Mock<ICardConnection>();
        var callCount = 0;

        mock.Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1) return [0x90, 0x00]; // SELECT OK

                // INIT UPDATE response: valid structure but wrong cryptogram
                var resp = new byte[30];
                resp[11] = 0x02; // SCP02
                // Sequence counter at [12..14], card challenge at [14..20]
                // Card cryptogram at [20..28] — all zeros = wrong
                resp[^2] = 0x90;
                resp[^1] = 0x00;
                return resp;
            });

        var ex = Assert.Throws<GlobalPlatformException>(() =>
            new GlobalPlatformService().EstablishSecureChannel(mock.Object, TestKey));

        Assert.That(ex!.Message, Does.Contain("cryptogram verification failed"));
    }

    [Test]
    public void EstablishSecureChannel_ThrowsOnSelectFailure()
    {
        var mock = new Mock<ICardConnection>();
        mock.Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Returns([0x6A, 0x82]); // File not found

        Assert.Throws<GlobalPlatformException>(() =>
            new GlobalPlatformService().EstablishSecureChannel(mock.Object, TestKey));
    }

    [Test]
    public void EstablishSecureChannel_ThrowsOnInitUpdateFailure()
    {
        var mock = new Mock<ICardConnection>();
        var callCount = 0;

        mock.Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Returns(() =>
            {
                callCount++;
                return callCount == 1
                    ? [0x90, 0x00]  // SELECT OK
                    : [0x69, 0x82]; // INIT UPDATE fails (security not satisfied)
                });

        Assert.Throws<GlobalPlatformException>(() =>
            new GlobalPlatformService().EstablishSecureChannel(mock.Object, TestKey));
    }

    [Test]
    public void Delete_SilentlySucceedsWhenAidNotFound()
    {
        var mock = new Mock<ICardConnection>();
        byte[] seq = [0x00, 0x01];
        var session = new Scp02Session();
        session.DeriveSessionKeys(TestKey, TestKey, TestKey, seq);

        // Return 6A88 (referenced data not found)
        mock.Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Returns([0x6A, 0x88]);

        Assert.DoesNotThrow(() =>
            new GlobalPlatformService().Delete(mock.Object, session, [0xA0, 0x00]));
    }

    [Test]
    public void Delete_ThrowsOnOtherErrors()
    {
        var mock = new Mock<ICardConnection>();
        byte[] seq = [0x00, 0x01];
        var session = new Scp02Session();
        session.DeriveSessionKeys(TestKey, TestKey, TestKey, seq);

        mock.Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Returns([0x69, 0x85]); // Conditions not satisfied

        Assert.Throws<GlobalPlatformException>(() =>
            new GlobalPlatformService().Delete(mock.Object, session, [0xA0, 0x00]));
    }

    [Test]
    public void Delete_SendsDeleteCommandWithAidTag()
    {
        var mock = new Mock<ICardConnection>();
        byte[] seq = [0x00, 0x01];
        var session = new Scp02Session();
        session.DeriveSessionKeys(TestKey, TestKey, TestKey, seq);
        byte[]? sentCommand = null;

        mock.Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Callback<byte[]>(cmd => sentCommand = cmd)
            .Returns([0x90, 0x00]);

        byte[] aid = [0xA0, 0x00, 0x00, 0x08, 0x98, 0x00, 0x00, 0x01];
        new GlobalPlatformService().Delete(mock.Object, session, aid);

        Assert.That(sentCommand, Is.Not.Null);
        // CLA should have secure messaging bit set
        Assert.That(sentCommand![0] & 0x04, Is.EqualTo(0x04));
        // INS should be DELETE (E4)
        Assert.That(sentCommand[1], Is.EqualTo(0xE4));
        // P2 should be 0x80 (delete related)
        Assert.That(sentCommand[3], Is.EqualTo(0x80));
        // Data should contain 4F tag + AID
        Assert.That(sentCommand[5], Is.EqualTo(0x4F));
        Assert.That(sentCommand[6], Is.EqualTo(0x08));
    }
}
