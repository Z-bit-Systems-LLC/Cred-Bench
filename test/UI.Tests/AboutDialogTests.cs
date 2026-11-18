using System.Windows;
using System.Windows.Media;
using CredBench.Windows.Views;
using FlaUI.Core.AutomationElements;
using NUnit.Framework;
using ZBitSystems.Wpf.UI.Controls;
using Window = FlaUI.Core.AutomationElements.Window;

namespace CredBench.UI.Tests;

[TestFixture]
public class AboutDialogTests : UiTestBase
{
    [TearDown]
    public void CloseDialog()
    {
        // A dialog left open would block every later fixture's restart of the app
        InvokeOnUI(() =>
        {
            foreach (var window in TestApp.Windows.OfType<AboutWindow>().ToList())
            {
                window.Close();
            }
        });
    }

    [Test]
    public void AboutOpensTheDialogAndCloseShutsIt()
    {
        Press("AboutButton");

        var dialog = WaitForDialog();
        Assert.That(dialog.Title, Is.EqualTo(Text("About_Title")));

        Find(dialog, "AboutCloseButton").AsButton().Invoke();

        WaitUntil(() => MainWindow!.ModalWindows.Length == 0, "Close should shut the dialog.");
    }

    [Test]
    public void ShowsTheVersionCopyrightAndLicenses()
    {
        Press("AboutButton");
        var dialog = WaitForDialog();

        // LicenseExpander has no automation peer, so read its loaded text in-process
        var licenses = InvokeOnUI(() => TestApp.Windows.OfType<AboutWindow>().Single()
            .FindVisualChildren<LicenseExpander>()
            .ToDictionary(expander => expander.Header, expander => expander.LicenseText));

        string[] headers = [Text("About_ApacheLicense"), Text("About_MITLicense"), Text("About_BSDLicense")];

        Assert.Multiple(() =>
        {
            Assert.That(Find(dialog, "AboutVersionText").Name, Does.StartWith("Version "));
            Assert.That(Find(dialog, "AboutCopyrightText").Name, Does.Contain("Z-bit Systems"));
            Assert.That(licenses.Keys, Is.EquivalentTo(headers));
            Assert.That(licenses[Text("About_ApacheLicense")], Does.Contain("Apache License"));
            Assert.That(licenses[Text("About_MITLicense")], Does.Contain("WPF UI"));
            Assert.That(licenses[Text("About_BSDLicense")], Does.Contain("PCSC-Sharp"));
        });
    }

    private Window WaitForDialog()
    {
        var dialog = AssemblySetup.Retry(() => MainWindow!.ModalWindows.FirstOrDefault(), TimeSpan.FromSeconds(10));
        Assert.That(dialog, Is.Not.Null, "The About dialog should open.");
        return dialog!;
    }

    private static AutomationElement Find(Window dialog, string automationId)
    {
        var element = AssemblySetup.Retry(
            () => dialog.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            TimeSpan.FromSeconds(5));
        Assert.That(element, Is.Not.Null, $"The About dialog should have {automationId}.");
        return element!;
    }
}

internal static class VisualTreeExtensions
{
    public static IEnumerable<T> FindVisualChildren<T>(this DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;
            foreach (var descendant in child.FindVisualChildren<T>())
                yield return descendant;
        }
    }
}
