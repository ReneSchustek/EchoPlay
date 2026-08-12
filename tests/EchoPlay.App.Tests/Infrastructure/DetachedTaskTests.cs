using EchoPlay.App.Infrastructure;
using EchoPlay.App.Tests.Fakes;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Infrastructure
{
    /// <summary>
    /// Sichert das Verhalten losgelöster Hintergrund-Aufgaben ab.
    /// <para>
    /// Anlass: Beim Schließen der Anwendung erschien eine <see cref="TaskCanceledException"/>,
    /// obwohl nichts schiefgegangen war — ein nebenher laufender Cover-Abruf wurde vom
    /// Abbruch-Token beendet, und niemand sah sich sein Ergebnis an.
    /// </para>
    /// </summary>
    public sealed class DetachedTaskTests
    {
        /// <summary>
        /// Der Kern: Ein Abbruch beim Beenden ist der Normalfall und darf den Nutzer nicht
        /// behelligen.
        /// </summary>
        [Fact]
        public async Task Observe_CancelledTask_IsSilent()
        {
            CapturingLogger logger = new();
            using CancellationTokenSource cts = new();
            await cts.CancelAsync();

            Task cancelled = Task.FromCanceled(cts.Token);
            DetachedTask.Observe(cancelled, logger, "Cover-Abruf");

            await WaitForCompletionAsync(cancelled);

            Assert.Empty(logger.Entries);
        }

        /// <summary>
        /// Ein echter Fehler im Hintergrund darf nicht spurlos verschwinden.
        /// </summary>
        [Fact]
        public async Task Observe_FailedTask_IsLogged()
        {
            CapturingLogger logger = new();
            Task failed = Task.FromException(new InvalidOperationException("Cover-Dienst antwortet nicht"));

            DetachedTask.Observe(failed, logger, "Cover-Abruf");

            await WaitForCompletionAsync(failed);

            (string Level, string Message, Exception? Exception) entry = Assert.Single(logger.Entries);
            Assert.Equal("Warning", entry.Level);
            Assert.Contains("Cover-Abruf", entry.Message, StringComparison.Ordinal);
        }

        /// <summary>
        /// Eine Abbruch-Ausnahme im Inneren zählt ebenfalls als Abbruch, nicht als Fehler.
        /// Genau so kommt sie an, wenn ein Abruf mitten im Netzzugriff abgebrochen wird.
        /// </summary>
        [Fact]
        public async Task Observe_TaskFailingWithCancellation_IsSilent()
        {
            CapturingLogger logger = new();
            Task failed = Task.FromException(new OperationCanceledException());

            DetachedTask.Observe(failed, logger, "Cover-Abruf");

            await WaitForCompletionAsync(failed);

            Assert.Empty(logger.Entries);
        }

        [Fact]
        public async Task Observe_SuccessfulTask_IsSilent()
        {
            CapturingLogger logger = new();
            Task done = Task.CompletedTask;

            DetachedTask.Observe(done, logger, "Cover-Abruf");

            await WaitForCompletionAsync(done);

            Assert.Empty(logger.Entries);
        }

        /// <summary>
        /// Die Ausnahme muss abgerufen werden — sonst gilt die Aufgabe weiterhin als
        /// unbeobachtet und schlägt später beim Einsammeln durch den Speicherbereiniger auf.
        /// </summary>
        [Fact]
        public async Task Observe_MarksTheFailureAsSeen()
        {
            CapturingLogger logger = new();
            Task failed = Task.FromException(new InvalidOperationException("Fehler"));

            DetachedTask.Observe(failed, logger, "Cover-Abruf");
            await WaitForCompletionAsync(failed);

            Assert.NotNull(failed.Exception);
        }

        [Fact]
        public void Observe_NullArguments_AreRejected()
        {
            CapturingLogger logger = new();

            _ = Assert.Throws<ArgumentNullException>(() => DetachedTask.Observe(null!, logger));
            _ = Assert.Throws<ArgumentNullException>(() => DetachedTask.Observe(Task.CompletedTask, null!));
        }

        /// <summary>
        /// Die Fortsetzung läuft ohne eigenen Zeitplan; ein kurzes Nachfassen genügt, damit
        /// sie sicher durchgelaufen ist, ohne den Test an eine feste Wartezeit zu binden.
        /// </summary>
        private static async Task WaitForCompletionAsync(Task task)
        {
            for (int attempt = 0; attempt < 50 && !task.IsCompleted; attempt++)
            {
                await Task.Delay(10);
            }

            // Der Fortsetzung noch einen Moment geben, falls sie erst nach dem Abschluss läuft.
            await Task.Delay(20);
        }
    }
}
