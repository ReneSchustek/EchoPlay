using EchoPlay.App.Helpers;
using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.App.Tests.Helpers
{
    /// <summary>
    /// Tests für <see cref="DialogReentrancyGuard"/>; der schützt WinUI 3 davor,
    /// zwei ContentDialoge gleichzeitig zu öffnen (COMException).
    /// Gemeinsame Test-Collection zwingt sequentielle Ausführung, weil
    /// der Guard einen statischen Zähler teilt.
    /// </summary>
    [Collection(DialogReentrancyGuardTestFixture.Name)]
    public sealed class DialogReentrancyGuardTests
    {
        [Fact]
        public void TryAcquire_WhenFree_ReturnsGuard()
        {
            using DialogReentrancyGuard? guard = DialogReentrancyGuard.TryAcquire();

            Assert.NotNull(guard);
        }

        [Fact]
        public void TryAcquire_WhenAlreadyHeld_ReturnsNull()
        {
            using DialogReentrancyGuard? first = DialogReentrancyGuard.TryAcquire();
            Assert.NotNull(first);

            DialogReentrancyGuard? second = DialogReentrancyGuard.TryAcquire();

            Assert.Null(second);
        }

        [Fact]
        public void TryAcquire_AfterDispose_SucceedsAgain()
        {
            DialogReentrancyGuard? first = DialogReentrancyGuard.TryAcquire();
            Assert.NotNull(first);
            first.Dispose();

            using DialogReentrancyGuard? second = DialogReentrancyGuard.TryAcquire();

            Assert.NotNull(second);
        }
    }

    /// <summary>
    /// Eigene Testsammlung ohne Parallellauf, weil der Wächter einen statischen Zähler teilt.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    [SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "xUnit-Collection-Definition muss public sein (xUnit1027).")]
    public sealed class DialogReentrancyGuardTestFixture
    {
        internal const string Name = "DialogReentrancyGuard";
    }
}
