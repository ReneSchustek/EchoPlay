using EchoPlay.Logger.Formatting;
using EchoPlay.Logger.Models;
using EchoPlay.Logger.Sinks;
using EchoPlay.Logger.Tests.Fakes;

namespace EchoPlay.Logger.Tests
{
    /// <summary>
    /// Tests für den <see cref="FileSink"/>.
    /// Prüft Datei-Erstellung, Inhalt und Verzeichnisverwaltung.
    /// Da FileSink auf das Dateisystem schreibt, verwendet jede Testinstanz ein eigenes temporäres Verzeichnis.
    /// </summary>
    public sealed class FileSinkTests : IDisposable
    {
        private readonly string _tempDirectory = Path.Combine(
            Path.GetTempPath(), "EchoPlayLoggerFileSinkTests");

        /// <summary>
        /// Erstellt ein sauberes temporäres Testverzeichnis.
        /// </summary>
        public FileSinkTests()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }

            _ = Directory.CreateDirectory(_tempDirectory);
        }

        /// <summary>
        /// Räumt das temporäre Testverzeichnis nach dem Test auf.
        /// </summary>
        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        /// <summary>
        /// WriteAsync erstellt eine .log-Datei im angegebenen Verzeichnis.
        /// </summary>
        [Fact]
        public async Task WriteAsync_ErstelltLogDateiImVerzeichnis()
        {
            string testDir = Path.Combine(_tempDirectory, "test_erstellt");
            _ = Directory.CreateDirectory(testDir);
            FileSink sink = new(testDir, new DefaultLogFormatter());
            LogEntry entry = new(
                Timestamp: new DateTime(2026, 3, 2, 14, 30, 45, 0),
                Level: LogLevel.Information,
                Message: "Testmeldung",
                Category: "TestKlasse",
                Scopes: []);

            await sink.WriteAsync(entry);

            string[] logFiles = Directory.GetFiles(testDir, "*.log");
            Assert.NotEmpty(logFiles);
        }

        /// <summary>
        /// WriteAsync schreibt Kategorie, Level und Nachricht in die Log-Datei.
        /// </summary>
        [Fact]
        public async Task WriteAsync_InhaltEnthältKategorieUndNachricht()
        {
            string testDir = Path.Combine(_tempDirectory, "test_inhalt");
            _ = Directory.CreateDirectory(testDir);
            FileSink sink = new(testDir, new DefaultLogFormatter());
            LogEntry entry = new(
                Timestamp: new DateTime(2026, 3, 2, 14, 30, 45, 0),
                Level: LogLevel.Warning,
                Message: "ProfilierungsMeldung",
                Category: "InhaltKlasse",
                Scopes: []);

            await sink.WriteAsync(entry);

            string[] logFiles = Directory.GetFiles(testDir, "*.log");
            string content = await File.ReadAllTextAsync(logFiles[0]);

            Assert.Contains("ProfilierungsMeldung", content, StringComparison.Ordinal);
            Assert.Contains("InhaltKlasse", content, StringComparison.Ordinal);
            Assert.Contains("Warning", content, StringComparison.Ordinal);
        }

        /// <summary>
        /// WriteAsync hängt mehrere Meldungen an dieselbe Tagesdatei an
        /// und überschreibt vorhandene Einträge nicht.
        /// </summary>
        [Fact]
        public async Task WriteAsync_HaengtMehrereMeldungenAnSelbeTagesdatei()
        {
            string testDir = Path.Combine(_tempDirectory, "test_append");
            _ = Directory.CreateDirectory(testDir);
            FileSink sink = new(testDir, new DefaultLogFormatter());

            LogEntry entry1 = new(
                Timestamp: new DateTime(2026, 3, 2, 10, 0, 0, 0),
                Level: LogLevel.Information,
                Message: "ErsteNachricht",
                Category: "Klasse",
                Scopes: []);

            LogEntry entry2 = new(
                Timestamp: new DateTime(2026, 3, 2, 10, 0, 1, 0),
                Level: LogLevel.Information,
                Message: "ZweiteNachricht",
                Category: "Klasse",
                Scopes: []);

            await sink.WriteAsync(entry1);
            await sink.WriteAsync(entry2);

            string[] logFiles = Directory.GetFiles(testDir, "*.log");
            _ = Assert.Single(logFiles);

            string content = await File.ReadAllTextAsync(logFiles[0]);
            Assert.Contains("ErsteNachricht", content, StringComparison.Ordinal);
            Assert.Contains("ZweiteNachricht", content, StringComparison.Ordinal);
        }

        /// <summary>
        /// Der FileSink-Konstruktor erstellt das Log-Verzeichnis, wenn es noch nicht existiert.
        /// </summary>
        [Fact]
        public void Konstruktor_ErstelltVerzeichnis_WennNichtVorhanden()
        {
            string neuesVerzeichnis = Path.Combine(_tempDirectory, "neues_log_verzeichnis");
            Assert.False(Directory.Exists(neuesVerzeichnis));

            _ = new FileSink(neuesVerzeichnis, new DefaultLogFormatter());

            Assert.True(Directory.Exists(neuesVerzeichnis));
        }

        /// <summary>
        /// Parallele WriteAsync-Aufrufer verzahnen Zeilen nicht — der SemaphoreSlim serialisiert
        /// den Datei-Zugriff, damit jede Log-Zeile vollständig und in genau einer Zeile steht.
        /// </summary>
        [Fact]
        public async Task WriteAsync_SerialisiertParalleleWrites()
        {
            string testDir = Path.Combine(_tempDirectory, "test_parallel");
            _ = Directory.CreateDirectory(testDir);
            using FileSink sink = new(testDir, new DefaultLogFormatter());

            const int writers = 20;
            Task[] writes = new Task[writers];
            for (int i = 0; i < writers; i++)
            {
                int idx = i;
                writes[i] = Task.Run(() => sink.WriteAsync(new LogEntry(
                    Timestamp: new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc).AddMilliseconds(idx),
                    Level: LogLevel.Information,
                    Message: $"Line-{idx:D2}",
                    Category: "Parallel",
                    Scopes: [])));
            }

            await Task.WhenAll(writes);

            string[] logFiles = Directory.GetFiles(testDir, "*.log");
            _ = Assert.Single(logFiles);

            string[] lines = await File.ReadAllLinesAsync(logFiles[0]);
            Assert.Equal(writers, lines.Length);
            for (int i = 0; i < writers; i++)
            {
                Assert.Contains(lines, l => l.Contains($"Line-{i:D2}", StringComparison.Ordinal));
            }
        }

        /// <summary>
        /// Nach Dispose werden neue Writes still ignoriert — kein ObjectDisposedException beim App-Shutdown.
        /// </summary>
        [Fact]
        public async Task WriteAsync_NachDispose_IstNoOp()
        {
            string testDir = Path.Combine(_tempDirectory, "test_disposed");
            _ = Directory.CreateDirectory(testDir);
            FileSink sink = new(testDir, new DefaultLogFormatter());
            sink.Dispose();

            await sink.WriteAsync(new LogEntry(
                Timestamp: DateTime.UtcNow,
                Level: LogLevel.Information,
                Message: "nach-dispose",
                Category: "Klasse",
                Scopes: []));

            Assert.Empty(Directory.GetFiles(testDir, "*.log"));
        }

        /// <summary>
        /// WriteAsync ruft den Formatter für jeden Eintrag auf.
        /// </summary>
        [Fact]
        public async Task WriteAsync_RuftFormatterFürJedenEintragAuf()
        {
            string testDir = Path.Combine(_tempDirectory, "test_formatter");
            _ = Directory.CreateDirectory(testDir);
            CapturingFormatter formatter = new();
            FileSink sink = new(testDir, formatter);

            LogEntry entry1 = new(
                Timestamp: new DateTime(2026, 3, 2, 10, 0, 0, 0),
                Level: LogLevel.Debug,
                Message: "Erste",
                Category: "Klasse",
                Scopes: []);

            LogEntry entry2 = new(
                Timestamp: new DateTime(2026, 3, 2, 10, 0, 1, 0),
                Level: LogLevel.Debug,
                Message: "Zweite",
                Category: "Klasse",
                Scopes: []);

            await sink.WriteAsync(entry1);
            await sink.WriteAsync(entry2);

            Assert.Equal(2, formatter.FormattedEntries.Count);
            Assert.Same(entry1, formatter.FormattedEntries[0]);
            Assert.Same(entry2, formatter.FormattedEntries[1]);
        }
    }
}
