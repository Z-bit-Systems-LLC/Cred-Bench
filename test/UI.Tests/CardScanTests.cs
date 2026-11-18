using CredBench.Core.Models;
using CredBench.Core.Models.TechnologyDetails;
using CredBench.Core.Services;
using Moq;
using NUnit.Framework;

namespace CredBench.UI.Tests;

/// <summary>
/// Scans a card through the scripted detectors and checks what the main window shows for it.
/// </summary>
/// <remarks>
/// Every test starts from a fresh app, because a scan leaves its result on screen and the
/// detectors keep the answer they were last given.
/// </remarks>
[TestFixture]
[NonParallelizable]
public class CardScanTests : UiTestBase
{
    private static readonly string[] AllTechnologyTabs =
        ["Tab_PIV", "Tab_DESFire", "Tab_ISO14443", "Tab_PKOC", "Tab_LEAFVerified", "Tab_LEAF"];

    [SetUp]
    public void StartFromAFreshAppEachTest()
    {
        AssemblySetup.EnsureFreshApp();
        AssemblySetup.BringToFront();
    }

    [Test]
    public void ScanShowsATabForEachDetectedTechnology()
    {
        TestApp.Detectors[CardTechnology.DESFire].Detects(new DESFireDetails
        {
            CardType = "DESFire EV3",
            Vendor = "NXP"
        });
        TestApp.Detectors[CardTechnology.ISO14443].Detects(new ISO14443Details
        {
            UID = TestApp.TestUid,
            Manufacturer = "NXP Semiconductors"
        });

        Press("ScanCardButton");

        WaitForStatus(Text("Status_Detected", "DESFire, ISO14443"));
        Assert.Multiple(() =>
        {
            Assert.That(IsShown("NoCardScannedText"), Is.False, "The no-card placeholder should be hidden.");
            Assert.That(IsShown("Tab_General"), Is.True, "General tab should be shown.");
            Assert.That(IsShown("Tab_DESFire"), Is.True, "DESFire tab should be shown.");
            Assert.That(IsShown("Tab_ISO14443"), Is.True, "ISO14443 tab should be shown.");
            Assert.That(IsShown("Tab_PIV"), Is.False, "PIV tab should be hidden.");
            Assert.That(IsShown("Tab_PKOC"), Is.False, "PKOC tab should be hidden.");
            Assert.That(IsShown("Tab_LEAF"), Is.False, "LEAF Universal tab should be hidden.");
            Assert.That(IsShown("Tab_LEAFVerified"), Is.False, "LEAF Verified tab should be hidden.");
        });
    }

    [Test]
    public void DetailTabsShowWhatTheDetectorsFound()
    {
        TestApp.Detectors[CardTechnology.DESFire].Detects(new DESFireDetails
        {
            CardType = "DESFire EV3",
            Vendor = "NXP"
        });

        Press("ScanCardButton");
        WaitForStatus(Text("Status_Detected", "DESFire"));

        SelectTab("Tab_General");
        WaitUntil(() => ShowsText(TestApp.DefaultReaders[0]),
            "The General tab should show the reader the card was read on.");

        SelectTab("Tab_DESFire");
        WaitUntil(() => ShowsText("DESFire EV3"), "The DESFire tab should show the card type.");
        WaitUntil(() => ShowsText("NXP"), "The DESFire tab should show the vendor.");
    }

    [Test]
    public void ScanOfAnUnknownCardShowsOnlyTheGeneralTab()
    {
        Press("ScanCardButton");

        WaitForStatus(Text("Status_TechUnknown"));
        Assert.Multiple(() =>
        {
            Assert.That(IsShown("Tab_General"), Is.True, "General tab should be shown.");
            foreach (var tab in AllTechnologyTabs)
            {
                Assert.That(IsShown(tab), Is.False, $"{tab} should be hidden.");
            }
        });
    }

    [Test]
    public void ScanThatCannotConnectShowsTheError()
    {
        TestApp.MockSmartCard
            .Setup(s => s.Connect(It.IsAny<string>()))
            .Throws(new InvalidOperationException("The reader is not responding."));

        Press("ScanCardButton");

        WaitForStatus(Text("Status_Error", "The reader is not responding."));
        Assert.That(IsShown("NoCardScannedText"), Is.True, "The no-card placeholder should be shown again.");
    }

    [Test]
    public void ScanHidesTheScanButtonUntilItFinishes()
    {
        using var release = new ManualResetEventSlim(false);
        TestApp.Detectors[CardTechnology.PIV].BlocksUntil(release);

        Press("ScanCardButton");

        try
        {
            WaitForStatus(Text("Status_ScanningCard"));
            Assert.That(IsShown("ScanCardButton"), Is.False, "Scan should be hidden while a scan runs.");
        }
        finally
        {
            release.Set();
        }

        WaitForStatus(Text("Status_TechUnknown"));
        WaitUntil(() => IsShown("ScanCardButton"), "Scan should come back once the scan finishes.");
    }

    [Test]
    public void CancellingAStuckScanReturnsToIdle()
    {
        using var release = new ManualResetEventSlim(false);
        TestApp.Detectors[CardTechnology.PIV].BlocksUntil(release);

        Press("ScanCardButton");

        try
        {
            WaitForStatus(Text("Status_ScanningCard"));
            Assert.That(IsShown("CancelScanButton"), Is.True, "Cancel should be shown while a scan runs.");

            // The detector is still blocked, as a stuck reader would be, when Cancel is pressed
            Press("CancelScanButton");

            WaitForStatus(Text("Status_ScanCancelledByUser"));
            WaitUntil(() => IsShown("ScanCardButton"), "Scan should come back once the scan is cancelled.");
            Assert.That(IsShown("CancelScanButton"), Is.False, "Cancel should be hidden once the scan is cancelled.");
        }
        finally
        {
            release.Set();
        }
    }

    [Test]
    public void InsertingACardScansIt()
    {
        TestApp.Detectors[CardTechnology.PIV].Detects(new PIVDetails());

        TestApp.MockSmartCard.Raise(s => s.CardInserted += null,
            new ReaderEventArgs { ReaderName = TestApp.DefaultReaders[0] });

        WaitForStatus(Text("Status_Detected", "PIV"));
        Assert.That(IsShown("Tab_PIV"), Is.True, "PIV tab should be shown.");
    }

    [Test]
    public void InsertingACardInAnotherReaderIsIgnored()
    {
        TestApp.MockSmartCard.Raise(s => s.CardInserted += null,
            new ReaderEventArgs { ReaderName = TestApp.DefaultReaders[1] });

        // Let the event reach the view model before checking nothing happened
        InvokeOnUI(() => { });
        Thread.Sleep(500);

        Assert.That(StatusText, Is.EqualTo(Text("Status_FoundReaders", TestApp.DefaultReaders.Count)));
        TestApp.MockSmartCard.Verify(s => s.Connect(It.IsAny<string>()), Times.Never);
    }
}
