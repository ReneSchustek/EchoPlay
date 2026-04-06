using EchoPlay.Logger.Models;
using EchoPlay.Logger.Sinks;
using EchoPlay.Logger.Tests.Fakes;

namespace EchoPlay.Logger.Tests
{
    /// <summary>
    /// Tests für den <see cref="DebugConsoleSink"/>.
    /// Prüft Fehlertoleranz und korrekte Formatter-Nutzung.
    /// </summary>
    public sealed class DebugConsoleSinkTests
    {
        /// <summary>Vorgefertigter Log-Eintrag für alle Tests dieser Klasse.</summary>
        private static readonly LogEntry TestEintrag = new(
            Timestamp: new DateTime(2026, 3, 2, 12, 0, 0, 0),
            Level: LogLevel.Information,
            Message: "Testmeldung",
            Category: "TestKlasse",
            Scopes: []);

        /// <summary>
        /// WriteAsync wirft keine Exception bei normalem Betrieb.
        /// </summary>
        [Fact]
        public async Task WriteAsync_WirftKeineException()
        {
            DebugConsoleSink sink = new(new CapturingFormatter());

            // Keine Exception erwartet
            await sink.WriteAsync(TestEintrag);
        }

        /// <summary>
        /// WriteAsync ruft den Formatter mit dem übergebenen Eintrag auf.
        /// </summary>
        [Fact]
        public async Task WriteAsync_RuftFormatterMitEintragAuf()
        {
            CapturingFormatter formatter = new();
            DebugConsoleSink sink = new(formatter);

            await sink.WriteAsync(TestEintrag);

            Assert.Single(formatter.FormattedEntries);
            Assert.Same(TestEintrag, formatter.FormattedEntries[0]);
        }

        /// <summary>
        /// WriteAsync propagiert keine Exception, wenn der Formatter einen Fehler wirft.
        /// Die Senke ist fehlertolerant gegenüber defekten Formatierern.
        /// </summary>
        [Fact]
        public async Task WriteAsync_MitWerfendemFormatter_WirftKeineException()
        {
            DebugConsoleSink sink = new(new ThrowingFormatter());

            // Fehlertoleranz: Formatter-Exception darf nicht propagiert werden
            await sink.WriteAsync(TestEintrag);
        }
    }
}
