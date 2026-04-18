using EchoPlay.App.Infrastructure;
using EchoPlay.App.Tests.Fakes;
using System;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Infrastructure
{
    /// <summary>
    /// Tests für <see cref="AsyncEventHandler.RunSafelyAsync"/> — das Sicherheitsnetz
    /// für async-void-UI-Handler. Jede Exception muss geloggt und als Dialog gezeigt
    /// werden; <see cref="OperationCanceledException"/> ist ein gewollter Abbruch.
    /// </summary>
    public sealed class AsyncEventHandlerTests
    {
        [Fact]
        public async Task RunSafelyAsync_HappyPath_CompletesWithoutLogOrDialog()
        {
            FakeErrorDialogService dialog = new();
            CapturingLogger logger = new();
            bool invoked = false;

            await AsyncEventHandler.RunSafelyAsync(
                () => { invoked = true; return Task.CompletedTask; },
                dialog,
                logger,
                "UnitTest");

            Assert.True(invoked);
            Assert.Empty(dialog.ShownDialogs);
            Assert.Empty(logger.Entries);
        }

        [Fact]
        public async Task RunSafelyAsync_OnException_LogsWarningAndShowsDialog()
        {
            FakeErrorDialogService dialog = new();
            CapturingLogger logger = new();

            await AsyncEventHandler.RunSafelyAsync(
                () => throw new InvalidOperationException("Boom"),
                dialog,
                logger,
                "BrokenClick");

            _ = Assert.Single(logger.Entries);
            Assert.Equal("Warning", logger.Entries[0].Level);
            Assert.Contains("BrokenClick", logger.Entries[0].Message, StringComparison.Ordinal);
            Assert.Contains("Boom", logger.Entries[0].Message, StringComparison.Ordinal);

            _ = Assert.Single(dialog.ShownDialogs);
            Assert.Contains("Boom", dialog.ShownDialogs[0].Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task RunSafelyAsync_OnOperationCanceled_SwallowsSilently()
        {
            FakeErrorDialogService dialog = new();
            CapturingLogger logger = new();

            await AsyncEventHandler.RunSafelyAsync(
                () => throw new OperationCanceledException(),
                dialog,
                logger,
                "CancelledOp");

            Assert.Empty(dialog.ShownDialogs);
            Assert.Empty(logger.Entries);
        }
    }
}
