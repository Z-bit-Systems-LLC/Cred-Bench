using CredBench.Core.Models.TechnologyDetails;
using CredBench.Core.Services;
using CredBench.Core.Services.Leaf;
using CredBench.Core.ViewModels;
using Moq;
using NUnit.Framework;

namespace CredBench.Core.Tests.Leaf;

[TestFixture]
public class LeafVerifiedViewModelTests
{
    private const string TapUrl = "https://d.leaf-community.com?uid=00";

    private static LeafVerifiedViewModel CreateViewModel()
    {
        var localization = new Mock<ILocalizationService>();
        localization.Setup(l => l.GetString(It.IsAny<string>())).Returns<string>(k => k);
        localization.Setup(l => l.GetString(It.IsAny<string>(), It.IsAny<object[]>())).Returns<string, object[]>((k, _) => k);
        return new LeafVerifiedViewModel(new LeafTrustStore(), new LeafPrintedIdLookup(), localization.Object);
    }

    [Test]
    public void FreshScanWithTapUrl_OffersLookupOnly()
    {
        var vm = CreateViewModel();

        vm.DetectedDetails = new LEAFVerifiedDetails { TapUrl = TapUrl };

        Assert.That(vm.ShowLookUpPrintedId, Is.True);
        Assert.That(vm.HasPrintedId, Is.False);
        Assert.That(vm.LookUpPrintedIdCommand.CanExecute(null), Is.True);
    }

    [Test]
    public void AfterSuccessfulLookup_ShowsPrintedIdAndHidesButton()
    {
        var vm = CreateViewModel();

        vm.DetectedDetails = new LEAFVerifiedDetails { TapUrl = TapUrl, PrintedId = "ABCDE-001", PrintedIdNote = "ok" };

        Assert.That(vm.ShowLookUpPrintedId, Is.False);
        Assert.That(vm.HasPrintedId, Is.True);
        Assert.That(vm.LookUpPrintedIdCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void AfterFailedLookup_HidesButtonAndShowsNote()
    {
        var vm = CreateViewModel();

        vm.DetectedDetails = new LEAFVerifiedDetails { TapUrl = TapUrl, PrintedIdNote = "single-use" };

        Assert.That(vm.ShowLookUpPrintedId, Is.False);
        Assert.That(vm.HasPrintedId, Is.False);
        Assert.That(vm.HasPrintedIdNote, Is.True);
    }

    [Test]
    public void CardWithoutTapUrl_OffersNothing()
    {
        var vm = CreateViewModel();

        vm.DetectedDetails = new LEAFVerifiedDetails();

        Assert.That(vm.ShowLookUpPrintedId, Is.False);
        Assert.That(vm.HasTapUrl, Is.False);
    }

    [Test]
    public void Rescan_OffersLookupAgain()
    {
        var vm = CreateViewModel();
        vm.DetectedDetails = new LEAFVerifiedDetails { TapUrl = TapUrl, PrintedId = "ABCDE-001", PrintedIdNote = "ok" };

        vm.DetectedDetails = new LEAFVerifiedDetails { TapUrl = TapUrl + "1" };

        Assert.That(vm.ShowLookUpPrintedId, Is.True);
    }

    [Test]
    public void W40Value_DefaultsToWiegandBits()
    {
        var vm = CreateViewModel();

        vm.DetectedDetails = new LEAFVerifiedDetails { OpenId = 1 };

        Assert.That(vm.W40Value, Is.EqualTo(new string('0', 38) + "10"));
    }

    [Test]
    public void W40Value_FollowsSelectedFormat()
    {
        var vm = CreateViewModel();
        vm.DetectedDetails = new LEAFVerifiedDetails { OpenId = 160000000000L };
        List<string?> changed = [];
        ((System.ComponentModel.INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.SelectedCredentialFormat = CredentialFormat.Hex;

        Assert.That(vm.W40Value, Is.EqualTo("4A817C8000"));
        Assert.That(changed, Does.Contain(nameof(LeafVerifiedViewModel.W40Value)));
    }

    [Test]
    public void W40Value_IsNull_WithoutOpenId()
    {
        var vm = CreateViewModel();

        vm.DetectedDetails = new LEAFVerifiedDetails();

        Assert.That(vm.W40Value, Is.Null);
    }
}
