using Moq;
using NUnit.Framework;

namespace CredBench.UI.Tests;

[TestFixture]
public class ReaderTests : UiTestBase
{
    [TearDown]
    public void RestoreDefaultReaders()
    {
        TestApp.MockSmartCard.Setup(s => s.GetReaders()).Returns(TestApp.DefaultReaders);
    }

    [Test]
    public void RefreshPicksUpNewReaders()
    {
        TestApp.MockSmartCard.Setup(s => s.GetReaders())
            .Returns([.. TestApp.DefaultReaders, "Test Reader 3"]);

        Press("RefreshReadersButton");

        WaitForStatus(Text("Status_FoundReaders", 3));
    }

    [Test]
    public void RefreshWithNoReadersSaysSo()
    {
        TestApp.MockSmartCard.Setup(s => s.GetReaders()).Returns([]);

        Press("RefreshReadersButton");

        WaitForStatus(Text("Status_NoReadersFound"));
    }

    [Test]
    public void ResetRestartsReaderMonitoring()
    {
        TestApp.MockReaderMonitor.Invocations.Clear();

        Press("ResetReaderButton");

        WaitForStatus(Text("Status_ReaderResetComplete"));
        TestApp.MockReaderMonitor.Verify(m => m.Restart(), Times.Once);
    }
}
