using FlaUI.Core.AutomationElements;
using NUnit.Framework;
using AppMainWindow = CredBench.Windows.Views.MainWindow;

namespace CredBench.UI.Tests;

[TestFixture]
[Category("Smoke")]
public class SmokeTests : UiTestBase
{
    [Test]
    public void ApplicationLaunches()
    {
        Assert.That(MainWindow, Is.Not.Null, "Main window should exist.");
        Assert.That(MainWindow!.IsAvailable, Is.True, "Main window should be available.");
    }

    [Test]
    public void OnlyTheTestAppsMainWindowIsOpen()
    {
        // The real App startup builds its own host with the PC/SC services; it must not run here
        var mainWindows = InvokeOnUI(() => TestApp.Windows.OfType<AppMainWindow>().Count());

        Assert.That(mainWindows, Is.EqualTo(1), "Only the test app's main window should be open.");
    }

    [Test]
    public void MainWindowHasReaderAndCardControls()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FindByAutomationId("ReaderComboBox"), Is.Not.Null, "Reader list should exist.");
            Assert.That(FindByAutomationId("RefreshReadersButton"), Is.Not.Null, "Refresh button should exist.");
            Assert.That(FindByAutomationId("ResetReaderButton"), Is.Not.Null, "Reset button should exist.");
            Assert.That(FindByAutomationId("ScanCardButton"), Is.Not.Null, "Scan button should exist.");
            Assert.That(FindByAutomationId("ProgramCardButton"), Is.Not.Null, "Program Card button should exist.");
            Assert.That(FindByAutomationId("StatusMessageText"), Is.Not.Null, "Status line should exist.");
            Assert.That(FindByAutomationId("AboutButton"), Is.Not.Null, "About button should exist.");
        });
    }

    [Test]
    public void StartsWithTheFirstReaderSelectedAndNoCard()
    {
        var readers = WaitForElement("ReaderComboBox")!.AsComboBox();

        Assert.Multiple(() =>
        {
            Assert.That(readers.SelectedItem?.Text, Is.EqualTo(TestApp.DefaultReaders[0]));
            Assert.That(StatusText, Is.EqualTo(Text("Status_FoundReaders", TestApp.DefaultReaders.Count)));
            Assert.That(IsShown("NoCardScannedText"), Is.True, "The no-card placeholder should be shown.");
            Assert.That(IsShown("DetailsTabControl"), Is.False, "No detail tabs should be shown before a scan.");
            Assert.That(FindByAutomationId("ScanCardButton")!.IsEnabled, Is.True, "Scan should be available.");
        });
    }
}
