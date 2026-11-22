using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Management;

namespace EchoPlay.Logger.Tests
{
    /// <summary>
    /// Tests für den <see cref="LogCleanupService"/>.
    /// Prüft Bereinigung nach Alter und Größe sowie Deaktivierungsverhalten.
    /// Da der Dienst auf das Dateisystem zugreift, verwendet jede Testinstanz ein eigenes temporäres Verzeichnis.
    /// </summary>
    // Bewusst DateTime.Now: Der Logger bleibt ohne IClock, damit er von keiner anderen
    // Schicht abhängt, LogCleanupService benutzt deshalb selbst DateTime.Now. Die
    // Cutoff-Berechnung ist relativ zu „jetzt", also müssen die Test-Dateien es auch sein.
    public sealed class LogCleanupServiceTests : IDisposable
    {
        private readonly string _tempDirectory = Path.Combine(
            Path.GetTempPath(), "EchoPlayLoggerCleanupTests");

        /// <summary>
        /// Erstellt ein sauberes temporäres Testverzeichnis.
        /// </summary>
        public LogCleanupServiceTests()
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
        /// Cleanup mit deaktiviertem AutoCleanup löscht keine Dateien,
        /// unabhängig von Alter und Größe.
        /// </summary>
        [Fact]
        public void Cleanup_WithAutoCleanupDisabled_DeletesNoFiles()
        {
            string logFile = Path.Combine(_tempDirectory, "2025-01-01.log");
            File.WriteAllText(logFile, "Alter Log-Eintrag");
            File.SetLastWriteTime(logFile, DateTime.Now.AddDays(-60));

            LoggerOptions options = new()
            {
                LogDirectory = _tempDirectory,
                EnableAutoCleanup = false,
                RetentionDays = 7
            };

            LogCleanupService service = new(options);
            service.Cleanup();

            Assert.True(File.Exists(logFile));
        }

        /// <summary>
        /// Liegen die JSON-Protokolle in einem eigenen Ordner, räumt die Bereinigung auch dort auf.
        /// </summary>
        [Fact]
        public void Cleanup_WithSeparateJsonDirectory_DeletesOldJsonFilesThere()
        {
            string jsonDirectory = Path.Combine(_tempDirectory, "json");
            _ = Directory.CreateDirectory(jsonDirectory);
            string oldJson = Path.Combine(jsonDirectory, "2025-01-01.jsonl");
            File.WriteAllText(oldJson, "{}");
            File.SetLastWriteTime(oldJson, DateTime.Now.AddDays(-60));

            LoggerOptions options = new()
            {
                LogDirectory = _tempDirectory,
                JsonLogDirectory = jsonDirectory,
                EnableAutoCleanup = true,
                RetentionDays = 7
            };

            new LogCleanupService(options).Cleanup();

            Assert.False(File.Exists(oldJson));
        }

        /// <summary>
        /// Cleanup mit nicht existierendem Verzeichnis wirft keine Exception.
        /// </summary>
        [Fact]
        public void Cleanup_WithMissingDirectory_DoesNotThrow()
        {
            LoggerOptions options = new()
            {
                LogDirectory = Path.Combine(_tempDirectory, "nicht_vorhanden"),
                EnableAutoCleanup = true,
                RetentionDays = 7
            };

            LogCleanupService service = new(options);

            // Keine Exception erwartet
            service.Cleanup();
        }

        /// <summary>
        /// Cleanup löscht Log-Dateien, deren Schreibzeit die konfigurierte Aufbewahrungsdauer überschreitet.
        /// </summary>
        [Fact]
        public void Cleanup_BeyondRetentionDays_DeletesOldFiles()
        {
            // Datei ist 10 Tage alt und überschreitet die konfigurierte Aufbewahrung von 7 Tagen
            string oldFile = Path.Combine(_tempDirectory, "2026-02-01.log");
            File.WriteAllText(oldFile, "Alter Eintrag");
            File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-10));

            LoggerOptions options = new()
            {
                LogDirectory = _tempDirectory,
                EnableAutoCleanup = true,
                RetentionDays = 7,
                MaxTotalSizeMb = 0  // Größenbereinigung deaktiviert
            };

            LogCleanupService service = new(options);
            service.Cleanup();

            Assert.False(File.Exists(oldFile));
        }

        /// <summary>
        /// Eine abgelaufene Datei, die sich nicht löschen lässt, bleibt stehen, ohne das Aufräumen
        /// der übrigen oder den Aufrufer zu stören.
        /// </summary>
        [Fact]
        public void Cleanup_WithAReadOnlyExpiredFile_KeepsItAndCarriesOn()
        {
            string locked = Path.Combine(_tempDirectory, "2026-01-01.log");
            string expired = Path.Combine(_tempDirectory, "2026-01-02.log");
            File.WriteAllText(locked, "schreibgeschützt");
            File.WriteAllText(expired, "alt");
            File.SetLastWriteTime(locked, DateTime.Now.AddDays(-10));
            File.SetLastWriteTime(expired, DateTime.Now.AddDays(-10));

            // Schreibgeschützt verweigert Windows das Löschen mit UnauthorizedAccessException.
            File.SetAttributes(locked, FileAttributes.ReadOnly);
            try
            {
                LoggerOptions options = new()
                {
                    LogDirectory = _tempDirectory,
                    EnableAutoCleanup = true,
                    RetentionDays = 7,
                    MaxTotalSizeMb = 0
                };

                new LogCleanupService(options).Cleanup();

                Assert.True(File.Exists(locked));
                Assert.False(File.Exists(expired));
            }
            finally
            {
                // Sonst ließe sich das Testverzeichnis beim Aufräumen nicht mehr löschen.
                File.SetAttributes(locked, FileAttributes.Normal);
            }
        }

        /// <summary>
        /// Cleanup lässt Log-Dateien unangetastet, die innerhalb der Aufbewahrungsdauer liegen.
        /// </summary>
        [Fact]
        public void Cleanup_WithinRetentionDays_DeletesNoFiles()
        {
            // Datei ist 1 Tag alt und liegt innerhalb der Aufbewahrung von 7 Tagen
            string currentLogFile = Path.Combine(_tempDirectory, "2026-03-01.log");
            File.WriteAllText(currentLogFile, "Aktueller Eintrag");
            File.SetLastWriteTime(currentLogFile, DateTime.Now.AddDays(-1));

            LoggerOptions options = new()
            {
                LogDirectory = _tempDirectory,
                EnableAutoCleanup = true,
                RetentionDays = 7,
                MaxTotalSizeMb = 0
            };

            LogCleanupService service = new(options);
            service.Cleanup();

            Assert.True(File.Exists(currentLogFile));
        }

        /// <summary>
        /// Cleanup löscht keine Dateien, wenn die Gesamtgröße weit unterhalb des konfigurierten Limits liegt.
        /// </summary>
        [Fact]
        public void Cleanup_BelowSizeLimit_DeletesNoFiles()
        {
            // Kleine Testdateien (wenige Bytes) liegen weit unter dem Limit von 100 MB
            string logFile1 = Path.Combine(_tempDirectory, "2026-03-01.log");
            string logFile2 = Path.Combine(_tempDirectory, "2026-03-02.log");
            File.WriteAllText(logFile1, "Eintrag 1");
            File.WriteAllText(logFile2, "Eintrag 2");

            LoggerOptions options = new()
            {
                LogDirectory = _tempDirectory,
                EnableAutoCleanup = true,
                RetentionDays = 0,       // Altersbereinigung deaktiviert
                MaxTotalSizeMb = 100     // Testdateien liegen weit darunter
            };

            LogCleanupService service = new(options);
            service.Cleanup();

            Assert.True(File.Exists(logFile1));
            Assert.True(File.Exists(logFile2));
        }

        /// <summary>
        /// Cleanup löscht alte Dateien und lässt neue Dateien stehen, wenn beide vorhanden sind.
        /// </summary>
        [Fact]
        public void Cleanup_WithOldAndNewFiles_DeletesOnlyOld()
        {
            string oldFile = Path.Combine(_tempDirectory, "alt.log");
            string newFile = Path.Combine(_tempDirectory, "neu.log");

            File.WriteAllText(oldFile, "Alter Eintrag");
            File.WriteAllText(newFile, "Neuer Eintrag");

            File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-30));
            File.SetLastWriteTime(newFile, DateTime.Now.AddDays(-1));

            LoggerOptions options = new()
            {
                LogDirectory = _tempDirectory,
                EnableAutoCleanup = true,
                RetentionDays = 7,
                MaxTotalSizeMb = 0
            };

            LogCleanupService service = new(options);
            service.Cleanup();

            Assert.False(File.Exists(oldFile));
            Assert.True(File.Exists(newFile));
        }
    }
}
