using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Management;

namespace EchoPlay.Logger.Core
{
    /// <summary>
    /// Koordiniert die Logger-Factory und den Cleanup-Service der Anwendung.
    /// </summary>
    public sealed class LoggerManager : IDisposable
    {
        private readonly LogCleanupService _cleanupService;
        private readonly LoggerOptions _options;
        private bool _disposed;

        /// <summary>
        /// Die Logger-Factory dieser Instanz.
        /// </summary>
        public ILoggerFactory Factory { get; }

        /// <summary>
        /// Verzeichnis, in das der FileSink Log-Dateien schreibt.
        /// Wird vom Log-Viewer benötigt, um verfügbare Dateien aufzulisten.
        /// </summary>
        public string LogDirectory => _options.LogDirectory;

        /// <summary>
        /// Initialisiert den LoggerManager.
        /// </summary>
        /// <param name="loggerFactory">Die Logger-Factory.</param>
        /// <param name="cleanupService">Der Cleanup-Service für alte Log-Dateien.</param>
        /// <param name="options">Die Logger-Optionen – werden nach DB-Init mit gespeichertem RetentionDays aktualisiert.</param>
        public LoggerManager(LoggerFactory loggerFactory, LogCleanupService cleanupService, LoggerOptions options)
        {
            Factory = loggerFactory;
            _cleanupService = cleanupService;
            _options = options;
        }

        /// <summary>
        /// Aktualisiert die Aufbewahrungszeit für Log-Dateien.
        /// Wird nach dem DB-Start aufgerufen, damit der in AppSettings gespeicherte Wert gilt.
        /// </summary>
        /// <param name="days">Aufbewahrungszeit in Tagen. Mindestwert: 1.</param>
        public void UpdateRetentionDays(int days)
        {
            if (days < 1)
            {
                return;
            }

            _options.RetentionDays = days;
        }

        /// <summary>
        /// Setzt das Mindest-Log-Level zur Laufzeit.
        /// Da alle Logger dieselbe <see cref="LoggerOptions"/>-Instanz halten, wirkt die Änderung
        /// sofort ohne Neustart – kein erneutes Erstellen der Logger-Instanzen nötig.
        /// </summary>
        /// <param name="level">Das neue Mindest-Level. Einträge unterhalb werden nicht mehr geschrieben.</param>
        public void UpdateMinimumLevel(Models.LogLevel level)
        {
            _options.MinimumLevel = level;
        }

        /// <summary>
        /// Gibt alle verwalteten Ressourcen frei und führt einmalig den Log-Cleanup durch.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cleanupService.Cleanup();
        }
    }
}
