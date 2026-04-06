using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Management;

namespace EchoPlay.Logger.Tests
{
    /// <summary>
    /// Tests für den <see cref="LogCleanupService"/>.
    /// Prüft Bereinigung nach Alter und Größe sowie Deaktivierungsverhalten.
    /// Da der Dienst auf das Dateisystem zugreift, verwendet jede Testinstanz ein eigenes temporäres Verzeichnis.
    /// </summary>
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

            Directory.CreateDirectory(_tempDirectory);
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
        public void Cleanup_MitDeaktiviertemAutoCleanup_LöschtKeineDateien()
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
        /// Cleanup mit nicht existierendem Verzeichnis wirft keine Exception.
        /// </summary>
        [Fact]
        public void Cleanup_MitNichtExistierendemVerzeichnis_WirftKeineException()
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
        public void Cleanup_ÜberschritteneRetentionDays_LöschtAlteDateien()
        {
            // Datei ist 10 Tage alt – überschreitet die konfigurierte Aufbewahrung von 7 Tagen
            string alteDatei = Path.Combine(_tempDirectory, "2026-02-01.log");
            File.WriteAllText(alteDatei, "Alter Eintrag");
            File.SetLastWriteTime(alteDatei, DateTime.Now.AddDays(-10));

            LoggerOptions options = new()
            {
                LogDirectory = _tempDirectory,
                EnableAutoCleanup = true,
                RetentionDays = 7,
                MaxTotalSizeMb = 0  // Größenbereinigung deaktiviert
            };

            LogCleanupService service = new(options);
            service.Cleanup();

            Assert.False(File.Exists(alteDatei));
        }

        /// <summary>
        /// Cleanup lässt Log-Dateien unangetastet, die innerhalb der Aufbewahrungsdauer liegen.
        /// </summary>
        [Fact]
        public void Cleanup_EinhaltungRetentionDays_LöschtKeineDateien()
        {
            // Datei ist 1 Tag alt – liegt innerhalb der Aufbewahrung von 7 Tagen
            string aktuelleLogDatei = Path.Combine(_tempDirectory, "2026-03-01.log");
            File.WriteAllText(aktuelleLogDatei, "Aktueller Eintrag");
            File.SetLastWriteTime(aktuelleLogDatei, DateTime.Now.AddDays(-1));

            LoggerOptions options = new()
            {
                LogDirectory = _tempDirectory,
                EnableAutoCleanup = true,
                RetentionDays = 7,
                MaxTotalSizeMb = 0
            };

            LogCleanupService service = new(options);
            service.Cleanup();

            Assert.True(File.Exists(aktuelleLogDatei));
        }

        /// <summary>
        /// Cleanup löscht keine Dateien, wenn die Gesamtgröße weit unterhalb des konfigurierten Limits liegt.
        /// </summary>
        [Fact]
        public void Cleanup_UnterSizelimit_LöschtKeineDateien()
        {
            // Kleine Testdateien (wenige Bytes) liegen weit unter dem Limit von 100 MB
            string logDatei1 = Path.Combine(_tempDirectory, "2026-03-01.log");
            string logDatei2 = Path.Combine(_tempDirectory, "2026-03-02.log");
            File.WriteAllText(logDatei1, "Eintrag 1");
            File.WriteAllText(logDatei2, "Eintrag 2");

            LoggerOptions options = new()
            {
                LogDirectory = _tempDirectory,
                EnableAutoCleanup = true,
                RetentionDays = 0,       // Altersbereinigung deaktiviert
                MaxTotalSizeMb = 100     // Testdateien liegen weit darunter
            };

            LogCleanupService service = new(options);
            service.Cleanup();

            Assert.True(File.Exists(logDatei1));
            Assert.True(File.Exists(logDatei2));
        }

        /// <summary>
        /// Cleanup löscht alte Dateien und lässt neue Dateien stehen, wenn beide vorhanden sind.
        /// </summary>
        [Fact]
        public void Cleanup_MitAltenUndNeuenDateien_LöschtNurAlte()
        {
            string alteDatei = Path.Combine(_tempDirectory, "alt.log");
            string neueDatei = Path.Combine(_tempDirectory, "neu.log");

            File.WriteAllText(alteDatei, "Alter Eintrag");
            File.WriteAllText(neueDatei, "Neuer Eintrag");

            File.SetLastWriteTime(alteDatei, DateTime.Now.AddDays(-30));
            File.SetLastWriteTime(neueDatei, DateTime.Now.AddDays(-1));

            LoggerOptions options = new()
            {
                LogDirectory = _tempDirectory,
                EnableAutoCleanup = true,
                RetentionDays = 7,
                MaxTotalSizeMb = 0
            };

            LogCleanupService service = new(options);
            service.Cleanup();

            Assert.False(File.Exists(alteDatei));
            Assert.True(File.Exists(neueDatei));
        }
    }
}
