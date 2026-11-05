using CredBench.Core.Services;
using CredBench.Core.Services.GlobalPlatform;
using CredBench.Core.Services.Leaf;
using CredBench.Core.Services.Pkoc;
using CredBench.Core.ViewModels;
using Moq;
using NUnit.Framework;

namespace CredBench.Core.Tests.Leaf;

[TestFixture]
public class ProgramCardViewModelTests
{
    private static ProgramCardViewModel CreateViewModel(LeafTrustStore? trustStore = null)
    {
        var localization = new Mock<ILocalizationService>();
        localization.Setup(l => l.GetString(It.IsAny<string>())).Returns<string>(k => k);
        localization.Setup(l => l.GetString(It.IsAny<string>(), It.IsAny<object[]>())).Returns<string, object[]>((k, _) => k);
        return new ProgramCardViewModel(new Mock<ISmartCardService>().Object, new PkocCardProgrammer(),
            new LeafCardProgrammer(), trustStore ?? new LeafTrustStore(), localization.Object);
    }

    private static string TempDirectory() =>
        Path.Combine(Path.GetTempPath(), "credbench-vm-" + Guid.NewGuid().ToString("N"));

    [Test]
    public void ProgramCard_PkocOnly_NeedsNoTestRoot()
    {
        var vm = CreateViewModel();
        vm.SelectedReader = "Reader";
        vm.ProgramLeaf = false;

        Assert.That(vm.ProgramCardCommand.CanExecute(null), Is.True);
    }

    [Test]
    public void ProgramCard_NothingTicked_IsDisabled()
    {
        var vm = CreateViewModel();
        vm.SelectedReader = "Reader";
        vm.ProgramPkoc = false;
        vm.ProgramLeaf = false;

        Assert.That(vm.ProgramCardCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void QueryCardContent_NeedsReader()
    {
        var vm = CreateViewModel();
        Assert.That(vm.QueryCardContentCommand.CanExecute(null), Is.False);

        vm.SelectedReader = "Reader";
        Assert.That(vm.QueryCardContentCommand.CanExecute(null), Is.True);
    }

    [Test]
    public void CardContent_RecognisesPkocAndLeafApplets()
    {
        static CardContentEntry Entry(string aid) => new() { Aid = Convert.FromHexString(aid), LifecycleState = 0x07, Privileges = 0 };
        var content = new CardContent
        {
            Packages = [],
            Applets = [Entry("A000000898000001"), Entry("F05A424C45414601")],
        };
        var leafOnly = content with { Applets = [Entry("F05A424C45414601")] };

        Assert.That(content.HasPkocApplet && content.HasLeafApplet, Is.True);
        Assert.That(leafOnly.HasPkocApplet, Is.False);
    }

    [Test]
    public void BothTicked_ByDefault()
    {
        var vm = CreateViewModel();

        Assert.That(vm.ProgramBoth, Is.True);
        vm.ProgramPkoc = false;
        Assert.That(vm.ProgramBoth, Is.False);
    }

    [TestCase("123456789", 123456789L)]
    [TestCase(" 42 ", 42L)]
    [TestCase("274877906943", 274877906943L)]
    [TestCase("274877906944", null)]
    [TestCase("1234567890123", null)]
    [TestCase("12a", null)]
    [TestCase("", null)]
    public void OpenIdText_IsParsedWithinThe38BitRange(string text, long? expected)
    {
        var vm = CreateViewModel();

        vm.OpenIdText = text;

        Assert.That(vm.ParsedOpenId, Is.EqualTo(expected));
        Assert.That(vm.OpenIdError is not null, Is.EqualTo(expected is null && text.Trim().Length > 0));
    }

    [Test]
    public void OpenIdPreview_ShowsW40()
    {
        var vm = CreateViewModel();

        vm.OpenIdText = "123456789";

        // The fake localization returns the key; the value itself is LEAFVerifiedDetails.ToW40
        Assert.That(vm.OpenIdPreview, Is.EqualTo("LEAFV_Prog_W40Preview"));
    }

    [Test]
    public void RandomOpenId_IsTwelveDigitsInRange()
    {
        var vm = CreateViewModel();

        for (var i = 0; i < 50; i++)
        {
            vm.RandomOpenIdCommand.Execute(null);
            Assert.That(vm.OpenIdText, Has.Length.EqualTo(12));
            Assert.That(vm.ParsedOpenId, Is.Not.Null);
        }
    }

    [Test]
    public void ProgramCard_NeedsReaderTestRootAndOpenId()
    {
        var vm = CreateViewModel();
        var directory = TempDirectory();
        try
        {
            vm.SelectedReader = "Reader";
            vm.OpenIdText = "1";
            Assert.That(vm.ProgramCardCommand.CanExecute(null), Is.False, "no test root");

            vm.CreateTestCa(directory);
            Assert.That(vm.ProgramCardCommand.CanExecute(null), Is.True);

            vm.OpenIdText = "x";
            Assert.That(vm.ProgramCardCommand.CanExecute(null), Is.False, "bad Open ID");

            vm.OpenIdText = "1";
            vm.SelectedReader = null;
            Assert.That(vm.ProgramCardCommand.CanExecute(null), Is.False, "no reader");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void CreateTestCa_SavesAndTrustsTheRoot()
    {
        var store = new LeafTrustStore();
        var vm = CreateViewModel(store);
        var directory = TempDirectory();
        try
        {
            vm.CreateTestCa(directory);

            Assert.That(vm.HasTestCa, Is.True);
            Assert.That(vm.TestCaDirectory, Is.EqualTo(directory));
            Assert.That(File.Exists(Path.Combine(directory, LeafTestCa.KeyFileName)), Is.True);
            Assert.That(store.TestRoots, Has.Count.EqualTo(1));
            Assert.That(vm.TestCaMessage, Is.EqualTo("LEAFV_Prog_TestRootCreated"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void CreateTestCa_ExistingRoot_IsNotOverwritten()
    {
        var vm = CreateViewModel();
        var directory = TempDirectory();
        try
        {
            using (var existing = LeafTestCa.Create())
                existing.Save(directory);
            var before = File.ReadAllText(Path.Combine(directory, LeafTestCa.KeyFileName));

            vm.CreateTestCa(directory);

            Assert.That(vm.HasTestCa, Is.False);
            Assert.That(vm.TestCaMessage, Is.EqualTo("LEAFV_Prog_TestRootExists"));
            Assert.That(File.ReadAllText(Path.Combine(directory, LeafTestCa.KeyFileName)), Is.EqualTo(before));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void LoadTestCa_UsesAndTrustsTheRoot()
    {
        var store = new LeafTrustStore();
        var vm = CreateViewModel(store);
        var directory = TempDirectory();
        try
        {
            using (var existing = LeafTestCa.Create("Loaded Root"))
                existing.Save(directory);

            vm.LoadTestCa(directory);

            Assert.That(vm.TestCaName, Is.EqualTo("Loaded Root"));
            Assert.That(store.TestRoots.Single().Name, Is.EqualTo("Loaded Root"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void RestoreTestCa_MissingFolder_LeavesNoneQuietly()
    {
        var vm = CreateViewModel();

        vm.RestoreTestCa(TempDirectory());

        Assert.That(vm.HasTestCa, Is.False);
        Assert.That(vm.TestCaMessage, Is.Null);
    }
}
