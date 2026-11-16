using CredBench.Windows.Views;
using FlaUI.Core.AutomationElements;
using NUnit.Framework;

namespace CredBench.UI.Tests;

[TestFixture]
public class ProgramCardDialogTests : UiTestBase
{
    [TearDown]
    public void CloseDialog()
    {
        // A dialog left open would block every later fixture's restart of the app
        InvokeOnUI(() =>
        {
            foreach (var window in TestApp.Windows.OfType<ProgramCardWindow>().ToList())
            {
                window.Close();
            }
        });
    }

    [Test]
    public void ProgramCardOpensTheDialogAndCloseShutsIt()
    {
        Press("ProgramCardButton");

        var dialog = WaitForDialog();
        Assert.That(dialog.Title, Is.EqualTo(Text("Prog_Title")));

        var close = AssemblySetup.Retry(
            () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("ProgramCardCloseButton")),
            TimeSpan.FromSeconds(5));
        Assert.That(close, Is.Not.Null, "The dialog should have a Close button.");
        close!.AsButton().Invoke();

        WaitUntil(() => MainWindow!.ModalWindows.Length == 0, "Close should shut the dialog.");
    }

    [Test]
    public void OpeningTheDialogReadsTheCardInTheSelectedReader()
    {
        TestApp.MockSmartCard.Invocations.Clear();

        Press("ProgramCardButton");
        WaitForDialog();

        WaitUntil(() => TestApp.MockSmartCard.Invocations.Any(i =>
                i.Method.Name == nameof(Core.Services.ISmartCardService.Connect)
                && Equals(i.Arguments[0], TestApp.DefaultReaders[0])),
            "Opening the dialog should query the card in the selected reader.");
    }

    private Window WaitForDialog()
    {
        var dialog = AssemblySetup.Retry(() => MainWindow!.ModalWindows.FirstOrDefault(), TimeSpan.FromSeconds(10));
        Assert.That(dialog, Is.Not.Null, "The Program Card dialog should open.");
        return dialog!;
    }
}
