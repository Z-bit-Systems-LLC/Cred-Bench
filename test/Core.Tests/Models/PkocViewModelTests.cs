using System.ComponentModel;
using CredBench.Core.Models.TechnologyDetails;
using CredBench.Core.ViewModels;
using NUnit.Framework;

namespace CredBench.Core.Tests.Models;

[TestFixture]
public class PkocViewModelTests
{
    private static readonly PKOCDetails Details = new()
    {
        PublicKeyHex = "04" + new string('0', 62) + "FF" + new string('0', 64)
    };

    [Test]
    public void DefaultsTo256BitWiegandBits()
    {
        var vm = new PkocViewModel { DetectedDetails = Details };

        Assert.That(vm.CredentialValue, Is.EqualTo(Details.Credential256Bits));
    }

    [Test]
    public void CredentialValue_FollowsSelectedSizeAndFormat()
    {
        var vm = new PkocViewModel { DetectedDetails = Details };

        vm.SelectedCredentialSize = PkocCredentialSize.Bits64;
        vm.SelectedCredentialFormat = CredentialFormat.Decimal;

        Assert.That(vm.CredentialValue, Is.EqualTo("255"));
    }

    [Test]
    public void CredentialValue_IsNull_WithoutDetectedCredential()
    {
        var vm = new PkocViewModel();

        Assert.That(vm.CredentialValue, Is.Null);
    }

    [TestCase(nameof(PkocViewModel.SelectedCredentialSize))]
    [TestCase(nameof(PkocViewModel.SelectedCredentialFormat))]
    [TestCase(nameof(PkocViewModel.DetectedDetails))]
    public void ChangingAnInput_RaisesCredentialValueChanged(string input)
    {
        var vm = new PkocViewModel();
        List<string?> changed = [];
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        switch (input)
        {
            case nameof(PkocViewModel.SelectedCredentialSize):
                vm.SelectedCredentialSize = PkocCredentialSize.Bits75;
                break;
            case nameof(PkocViewModel.SelectedCredentialFormat):
                vm.SelectedCredentialFormat = CredentialFormat.Hex;
                break;
            default:
                vm.DetectedDetails = Details;
                break;
        }

        Assert.That(changed, Does.Contain(nameof(PkocViewModel.CredentialValue)));
    }
}
