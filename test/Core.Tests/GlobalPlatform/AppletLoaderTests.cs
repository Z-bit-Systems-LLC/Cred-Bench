using CredBench.Core.Services;
using CredBench.Core.Services.GlobalPlatform;
using Moq;
using NUnit.Framework;

namespace CredBench.Core.Tests.GlobalPlatform;

[TestFixture]
public class AppletLoaderTests
{
    private static readonly byte[] TestKey =
    [
        0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47,
        0x48, 0x49, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F
    ];

    private static readonly byte[] PackageAid = [0xA0, 0x00, 0x00, 0x08, 0x98, 0x00, 0x00, 0x02];
    private static readonly byte[] AppletAid = [0xA0, 0x00, 0x00, 0x08, 0x98, 0x00, 0x00, 0x01];

    [Test]
    public void WrapWithC4Tag_SmallData_UsesShortLength()
    {
        var data = new byte[100];
        var wrapped = AppletLoader.WrapWithC4Tag(data);

        Assert.That(wrapped[0], Is.EqualTo(0xC4));
        Assert.That(wrapped[1], Is.EqualTo(100));
        Assert.That(wrapped.Length, Is.EqualTo(102));
    }

    [Test]
    public void WrapWithC4Tag_MediumData_Uses81LengthEncoding()
    {
        var data = new byte[200];
        var wrapped = AppletLoader.WrapWithC4Tag(data);

        Assert.That(wrapped[0], Is.EqualTo(0xC4));
        Assert.That(wrapped[1], Is.EqualTo(0x81));
        Assert.That(wrapped[2], Is.EqualTo(200));
        Assert.That(wrapped.Length, Is.EqualTo(203));
    }

    [Test]
    public void WrapWithC4Tag_LargeData_Uses82LengthEncoding()
    {
        var data = new byte[12000]; // typical CAP file size
        var wrapped = AppletLoader.WrapWithC4Tag(data);

        Assert.That(wrapped[0], Is.EqualTo(0xC4));
        Assert.That(wrapped[1], Is.EqualTo(0x82));
        Assert.That(wrapped[2], Is.EqualTo(12000 >> 8));
        Assert.That(wrapped[3], Is.EqualTo(12000 & 0xFF));
        Assert.That(wrapped.Length, Is.EqualTo(12004));
    }

    [Test]
    public void LoadAndInstall_SendsInstallForLoadFirst()
    {
        var mock = new Mock<ICardConnection>();
        var commands = new List<byte[]>();
        byte[] seq = [0x00, 0x01];
        var session = new Scp02Session();
        session.DeriveSessionKeys(TestKey, TestKey, TestKey, seq);

        mock.Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Callback<byte[]>(cmd => commands.Add((byte[])cmd.Clone()))
            .Returns([0x90, 0x00]);

        var loader = new AppletLoader();
        loader.LoadAndInstall(mock.Object, session, new byte[100],
            PackageAid, AppletAid, AppletAid);

        Assert.That(commands.Count, Is.GreaterThanOrEqualTo(3));
        // First command: INSTALL [for load] — INS=E6, P1=02
        Assert.That(commands[0][1], Is.EqualTo(0xE6));
    }

    [Test]
    public void LoadAndInstall_SendsLoadBlocks()
    {
        var mock = new Mock<ICardConnection>();
        var commands = new List<byte[]>();
        byte[] seq = [0x00, 0x01];
        var session = new Scp02Session();
        session.DeriveSessionKeys(TestKey, TestKey, TestKey, seq);

        mock.Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Callback<byte[]>(cmd => commands.Add((byte[])cmd.Clone()))
            .Returns([0x90, 0x00]);

        // 500 bytes + C4 TLV header → should be 3 LOAD blocks (240+240+remaining)
        var capData = new byte[500];
        var loader = new AppletLoader();
        loader.LoadAndInstall(mock.Object, session, capData,
            PackageAid, AppletAid, AppletAid);

        // Commands: INSTALL [for load], LOAD blocks, INSTALL [for install]
        var loadCommands = commands.Where(c => c[1] == 0xE8).ToList();
        Assert.That(loadCommands.Count, Is.GreaterThanOrEqualTo(2));

        // Last LOAD block should have P1=0x80
        Assert.That(loadCommands[^1][2], Is.EqualTo(0x80), "Last LOAD block P1 should be 0x80");

        // Non-last blocks should have P1=0x00
        for (int i = 0; i < loadCommands.Count - 1; i++)
            Assert.That(loadCommands[i][2], Is.EqualTo(0x00));
    }

    [Test]
    public void LoadAndInstall_SendsInstallForInstallLast()
    {
        var mock = new Mock<ICardConnection>();
        var commands = new List<byte[]>();
        byte[] seq = [0x00, 0x01];
        var session = new Scp02Session();
        session.DeriveSessionKeys(TestKey, TestKey, TestKey, seq);

        mock.Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Callback<byte[]>(cmd => commands.Add((byte[])cmd.Clone()))
            .Returns([0x90, 0x00]);

        var loader = new AppletLoader();
        loader.LoadAndInstall(mock.Object, session, new byte[100],
            PackageAid, AppletAid, AppletAid);

        // Last command: INSTALL [for install] — INS=E6, P1=0C
        var lastE6 = commands.Last(c => c[1] == 0xE6);
        // P1=0x0C is in the original command, but after wrapping CLA changes.
        // Check the P1 in the wrapped command (position 2)
        // Actually, P1 doesn't change during wrapping — only CLA and Lc change
        // But we capture the wrapped command, so P1 is still at index 2
        // However, the first INSTALL has P1=0x02 and last has P1=0x0C
        // Let's check the last command that has INS=0xE6
        var installCommands = commands.Where(c => c[1] == 0xE6).ToList();
        Assert.That(installCommands.Count, Is.EqualTo(2));
        Assert.That(installCommands[^1][2], Is.EqualTo(0x0C),
            "Last INSTALL should be 'for install and make selectable'");
    }

    [Test]
    public void LoadAndInstall_ReportsProgress()
    {
        var mock = new Mock<ICardConnection>();
        byte[] seq = [0x00, 0x01];
        var session = new Scp02Session();
        session.DeriveSessionKeys(TestKey, TestKey, TestKey, seq);

        mock.Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Returns([0x90, 0x00]);

        var progressReports = new List<(int Current, int Total)>();
        var progress = new SyncProgress<(int, int)>(p => progressReports.Add(p));

        var loader = new AppletLoader();
        loader.LoadAndInstall(mock.Object, session, new byte[500],
            PackageAid, AppletAid, AppletAid, progress);

        Assert.That(progressReports.Count, Is.GreaterThan(0));
    }

    /// <summary>
    /// Synchronous IProgress implementation for testing (Progress&lt;T&gt; posts asynchronously).
    /// </summary>
    private class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    [Test]
    public void LoadAndInstall_ThrowsOnLoadBlockFailure()
    {
        var mock = new Mock<ICardConnection>();
        var callCount = 0;
        byte[] seq = [0x00, 0x01];
        var session = new Scp02Session();
        session.DeriveSessionKeys(TestKey, TestKey, TestKey, seq);

        mock.Setup(x => x.Transmit(It.IsAny<byte[]>()))
            .Returns(() =>
            {
                callCount++;
                // Fail on the 3rd command (first LOAD block)
                return callCount >= 3 ? [0x69, 0x85] : (byte[])[0x90, 0x00];
            });

        Assert.Throws<GlobalPlatformException>(() =>
            new AppletLoader().LoadAndInstall(mock.Object, session, new byte[500],
                PackageAid, AppletAid, AppletAid));
    }
}
