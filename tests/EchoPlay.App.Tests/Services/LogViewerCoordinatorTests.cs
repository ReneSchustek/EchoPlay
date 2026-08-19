using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Core;
using EchoPlay.Logger.Management;
using EchoPlay.Logger.Models;
using EchoPlay.Logger.Sinks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="LogViewerCoordinator"/> — Filter, Aufbereitung der Zeilen und
    /// die beiden Wege, die ohne vorhandene Dateien auskommen.
    /// </summary>
    /// <remarks>
    /// Das Anlegen echter Protokolldateien bleibt außen vor. Die beiden Fälle, in denen
    /// nichts da ist — kein Verzeichnis, keine Datei — sind trotzdem prüfbar, und genau
    /// sie treffen den Anwender: beim ersten Start und wenn eine Datei gerade rotiert.
    /// </remarks>
    public sealed class LogViewerCoordinatorTests
    {
        private static LoggerManager BuildLoggerManager()
        {
            // Minimaler LoggerManager ohne Dateilogging — Coordinator nutzt nur LogDirectory.
            string tmp = Path.Combine(Path.GetTempPath(), "echoplay-tests-" + Guid.NewGuid().ToString("N"));
            LoggerOptions options = new() { LogDirectory = tmp, EnableFileLogging = false, EnableAutoCleanup = false };
            LoggerFactory factory = new([], options);
            LogCleanupService cleanup = new(options);
            return new LoggerManager(factory, cleanup, options);
        }

        [Fact]
        public void IsLiveViewAvailable_WithMemorySink_IsTrue()
        {
            using LoggerManager manager = BuildLoggerManager();
            MemorySink sink = new(capacity: 100);
            LogViewerCoordinator coordinator = new(manager, sink);

            Assert.True(coordinator.IsLiveViewAvailable);
        }

        [Fact]
        public void IsLiveViewAvailable_WithoutMemorySink_IsFalse()
        {
            using LoggerManager manager = BuildLoggerManager();
            LogViewerCoordinator coordinator = new(manager, memorySink: null);

            Assert.False(coordinator.IsLiveViewAvailable);
        }

        [Fact]
        public async Task BuildFilteredLiveEntries_FiltersByMinimumLevel()
        {
            using LoggerManager manager = BuildLoggerManager();
            MemorySink sink = new(capacity: 10);
            await sink.WriteAsync(new LogEntry(DateTime.UtcNow, LogLevel.Debug, "DBG", "X", []));
            await sink.WriteAsync(new LogEntry(DateTime.UtcNow, LogLevel.Error, "ERR", "X", []));
            LogViewerCoordinator coordinator = new(manager, sink);

            IReadOnlyList<string> entries = coordinator.BuildFilteredLiveEntries(string.Empty, LogLevel.Warning);

            string only = Assert.Single(entries);
            Assert.Contains("ERR", only, StringComparison.Ordinal);
        }

        [Fact]
        public void BuildFilteredLiveEntries_WithoutMemorySink_IsEmpty()
        {
            using LoggerManager manager = BuildLoggerManager();
            LogViewerCoordinator coordinator = new(manager, memorySink: null);

            // Ohne Puffer gibt es nichts anzuzeigen. Eine leere Liste ist hier die
            // richtige Antwort — nicht ein Fehler, der die Ansicht aufreißt.
            Assert.Empty(coordinator.BuildFilteredLiveEntries(string.Empty, LogLevel.Debug));
        }

        [Fact]
        public async Task BuildFilteredLiveEntries_SearchText_IgnoresCase()
        {
            using LoggerManager manager = BuildLoggerManager();
            MemorySink sink = new(capacity: 10);
            await sink.WriteAsync(new LogEntry(DateTime.UtcNow, LogLevel.Information, "Import", "Import abgeschlossen", []));
            await sink.WriteAsync(new LogEntry(DateTime.UtcNow, LogLevel.Information, "Player", "Wiedergabe gestartet", []));
            LogViewerCoordinator coordinator = new(manager, sink);

            IReadOnlyList<string> entries = coordinator.BuildFilteredLiveEntries("IMPORT", LogLevel.Debug);

            // Der Suchtext kommt aus einem Eingabefeld. Wer dort „import" tippt, erwartet die
            // Zeile mit „Import" — auf die Schreibweise achtet beim Suchen niemand.
            string only = Assert.Single(entries);
            Assert.Contains("Import", only, StringComparison.Ordinal);
        }

        [Fact]
        public async Task BuildFilteredLiveEntries_SearchTextWithoutMatch_IsEmpty()
        {
            using LoggerManager manager = BuildLoggerManager();
            MemorySink sink = new(capacity: 10);
            await sink.WriteAsync(new LogEntry(DateTime.UtcNow, LogLevel.Information, "Import", "Import abgeschlossen", []));
            LogViewerCoordinator coordinator = new(manager, sink);

            Assert.Empty(coordinator.BuildFilteredLiveEntries("kommt so nicht vor", LogLevel.Debug));
        }

        [Fact]
        public async Task BuildFilteredLiveEntries_CombinesLevelAndSearch()
        {
            using LoggerManager manager = BuildLoggerManager();
            MemorySink sink = new(capacity: 10);
            await sink.WriteAsync(new LogEntry(DateTime.UtcNow, LogLevel.Debug, "Import", "Import begonnen", []));
            await sink.WriteAsync(new LogEntry(DateTime.UtcNow, LogLevel.Error, "Import", "Import fehlgeschlagen", []));
            await sink.WriteAsync(new LogEntry(DateTime.UtcNow, LogLevel.Error, "Player", "Wiedergabe fehlgeschlagen", []));
            LogViewerCoordinator coordinator = new(manager, sink);

            IReadOnlyList<string> entries = coordinator.BuildFilteredLiveEntries("Import", LogLevel.Error);

            // Beide Filter gelten zusammen. Griffe nur der zuletzt gesetzte, zeigte die
            // Ansicht je nach Reihenfolge der Eingabe etwas anderes an.
            string only = Assert.Single(entries);
            Assert.Contains("fehlgeschlagen", only, StringComparison.Ordinal);
        }

        // ── Aufbereitung der Zeile ───────────────────────────────────────────────

        [Theory]
        [InlineData(LogLevel.Trace, "TRACE")]
        [InlineData(LogLevel.Debug, "DEBUG")]
        [InlineData(LogLevel.Information, "INFO")]
        [InlineData(LogLevel.Warning, "WARN")]
        [InlineData(LogLevel.Error, "ERROR")]
        [InlineData(LogLevel.Fatal, "FATAL")]
        public async Task BuildFilteredLiveEntries_NamesEveryLevel(LogLevel level, string expectedTag)
        {
            using LoggerManager manager = BuildLoggerManager();
            MemorySink sink = new(capacity: 10);
            await sink.WriteAsync(new LogEntry(DateTime.UtcNow, level, "Nachricht", "Kategorie", []));
            LogViewerCoordinator coordinator = new(manager, sink);

            string only = Assert.Single(coordinator.BuildFilteredLiveEntries(string.Empty, LogLevel.Trace));

            // Das Kürzel ist die einzige Stelle, an der die Stufe sichtbar wird. Fehlte eine,
            // stünde dort ein Platzhalter, und die Zeile wäre nicht mehr einzuordnen.
            Assert.Contains($"[{expectedTag}", only, StringComparison.Ordinal);
        }

        [Fact]
        public async Task BuildFilteredLiveEntries_ShowsTimeCategoryAndMessage()
        {
            using LoggerManager manager = BuildLoggerManager();
            MemorySink sink = new(capacity: 10);
            DateTime timestamp = new(2026, 8, 17, 14, 30, 5, DateTimeKind.Utc);
            await sink.WriteAsync(new LogEntry(timestamp, LogLevel.Information, "Import abgeschlossen", "ImportService", []));
            LogViewerCoordinator coordinator = new(manager, sink);

            string only = Assert.Single(coordinator.BuildFilteredLiveEntries(string.Empty, LogLevel.Trace));

            // Der Zeitstempel wird als Weltzeit geführt und für die Anzeige umgerechnet.
            // Stünde dort die Weltzeit, passte die Uhrzeit im Protokoll nicht zu der,
            // die der Anwender beim Auftreten des Fehlers auf der Uhr hatte.
            string expectedTime = timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            Assert.StartsWith(expectedTime, only, StringComparison.Ordinal);
            Assert.Contains("ImportService: Import abgeschlossen", only, StringComparison.Ordinal);
        }

        [Fact]
        public async Task BuildFilteredLiveEntries_AppendsTheError()
        {
            using LoggerManager manager = BuildLoggerManager();
            MemorySink sink = new(capacity: 10);
            await sink.WriteAsync(new LogEntry(
                DateTime.UtcNow, LogLevel.Error, "Lesen fehlgeschlagen", "Import", [],
                new InvalidOperationException("Datei gesperrt")));
            LogViewerCoordinator coordinator = new(manager, sink);

            string only = Assert.Single(coordinator.BuildFilteredLiveEntries(string.Empty, LogLevel.Trace));

            // Ohne Art und Text des Fehlers steht in der Zeile nur, dass etwas schieflief.
            // Die Ursache liest man dann nirgends.
            Assert.Contains("InvalidOperationException: Datei gesperrt", only, StringComparison.Ordinal);
        }

        [Fact]
        public async Task BuildFilteredLiveEntries_SearchesTheWholeLineNotJustTheMessage()
        {
            using LoggerManager manager = BuildLoggerManager();
            MemorySink sink = new(capacity: 10);
            await sink.WriteAsync(new LogEntry(DateTime.UtcNow, LogLevel.Information, "Fertig", "ImportService", []));
            LogViewerCoordinator coordinator = new(manager, sink);

            // Wer nach der Herkunft sucht, tippt den Namen des Dienstes ein — nicht den
            // Text der Meldung, den er ja gerade sucht.
            _ = Assert.Single(coordinator.BuildFilteredLiveEntries("ImportService", LogLevel.Trace));
        }

        // ── Wenn nichts da ist ───────────────────────────────────────────────────

        [Fact]
        public async Task LoadLogFileOptions_WithoutAnyLogDirectory_OffersOnlyTheLiveView()
        {
            using LoggerManager manager = BuildLoggerManager();
            LogViewerCoordinator coordinator = new(manager, new MemorySink(capacity: 10));

            IReadOnlyList<LogFileOption> options =
                await coordinator.LoadLogFileOptionsAsync(TestContext.Current.CancellationToken);

            // Beim ersten Start gibt es noch keine Datei. Eine leere Auswahlliste sähe aus,
            // als sei die Protokollierung kaputt.
            LogFileOption only = Assert.Single(options);
            Assert.Null(only.FilePath);
        }

        [Fact]
        public async Task LoadFileLines_WhenTheFileIsGone_ReturnsNothing()
        {
            using LoggerManager manager = BuildLoggerManager();
            LogViewerCoordinator coordinator = new(manager, memorySink: null);

            string missing = Path.Combine(manager.LogDirectory, "echoplay-2020-01-01.log");

            IReadOnlyList<string> lines =
                await coordinator.LoadFileLinesAsync(missing, TestContext.Current.CancellationToken);

            // Die Auswahlliste kann eine Datei nennen, die inzwischen weggeräumt wurde.
            // Ein Absturz an dieser Stelle nähme dem Anwender die Ansicht, mit der er
            // gerade einem Fehler nachgeht.
            Assert.Empty(lines);
        }
    }
}
