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
        private static readonly LogEntry TestEntry = new(
            Timestamp: new DateTime(2026, 3, 2, 12, 0, 0, 0),
            Level: LogLevel.Information,
            Message: "Testmeldung",
            Category: "TestKlasse",
            Scopes: []);

        /// <summary>
        /// WriteAsync wirft keine Exception bei normalem Betrieb.
        /// </summary>
        [Fact]
        public async Task WriteAsync_DoesNotThrow()
        {
            DebugConsoleSink sink = new(new CapturingFormatter());

            // Keine Exception erwartet
            await sink.WriteAsync(TestEntry);
        }

        /// <summary>
        /// WriteAsync ruft den Formatter mit dem übergebenen Eintrag auf.
        /// </summary>
        [Fact]
        public async Task WriteAsync_CallsFormatterWithEntry()
        {
            CapturingFormatter formatter = new();
            DebugConsoleSink sink = new(formatter);

            await sink.WriteAsync(TestEntry);

            _ = Assert.Single(formatter.FormattedEntries);
            Assert.Same(TestEntry, formatter.FormattedEntries[0]);
        }

        /// <summary>
        /// WriteAsync propagiert keine Exception, wenn der Formatter einen Fehler wirft.
        /// Die Senke ist fehlertolerant gegenüber defekten Formatierern.
        /// </summary>
        [Fact]
        public async Task WriteAsync_WithThrowingFormatter_DoesNotThrow()
        {
            DebugConsoleSink sink = new(new ThrowingFormatter());

            // Fehlertoleranz: Formatter-Exception darf nicht propagiert werden
            await sink.WriteAsync(TestEntry);
        }
    }
}
