using System.Diagnostics.CodeAnalysis;
using EchoPlay.Logger.Configuration;

namespace EchoPlay.Logger.Management
{
    /// <summary>
    /// Bereinigt alte Log-Dateien nach Alter und Gesamtgröße.
    /// </summary>
    /// <remarks>
    /// Erstellt einen neuen LogCleanupService.
    /// </remarks>
    /// <param name="options">Die Logger-Konfiguration.</param>
    public sealed class LogCleanupService(LoggerOptions options)
    {
        private readonly LoggerOptions _options = options;

        /// <summary>
        /// Führt die Bereinigung durch.
        /// Löscht Logs nach Alter und Gesamtgröße.
        /// </summary>
        public void Cleanup()
        {
            if (!_options.EnableAutoCleanup)
            {
                return;
            }

            if (!Directory.Exists(_options.LogDirectory))
            {
                return;
            }

            // Beide Sink-Dateitypen berücksichtigen: FileSink (.log) und JsonLogSink (.jsonl).
            // Sonst wachsen die JSON-Logs unbegrenzt, weil sie nie bereinigt würden.
            string[] logPatterns = ["*.log", "*.jsonl"];

            List<FileInfo> logFiles = [.. logPatterns
                .SelectMany(pattern => Directory.GetFiles(_options.LogDirectory, pattern))
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.LastWriteTime)];

            // Bereinigen nach Alter
            if (_options.RetentionDays > 0)
            {
                // Lokalzeit, weil Log-Dateien lokale Zeitstempel im Namen tragen — UTC wäre
                // beim Vergleich mit File.LastWriteTime (Lokalzeit) fehleranfällig.
                DateTime cutoffDate = DateTime.Now.AddDays(-_options.RetentionDays);
                logFiles = CleanupByAge(logFiles, cutoffDate);
            }

            // Bereinigen nach Gesamtgröße
            if (_options.MaxTotalSizeMb > 0)
            {
                long maxSize = _options.MaxTotalSizeMb * 1024L * 1024L;
                CleanupBySize(logFiles, maxSize);
            }
        }

        /// <summary>
        /// Löscht Dateien die älter als das Cutoff-Datum sind.
        /// </summary>
        /// <param name="files">Liste der Log-Dateien.</param>
        /// <param name="cutoffDate">Dateien älter als dieses Datum werden gelöscht.</param>
        /// <returns>Liste der verbleibenden Dateien.</returns>
        private static List<FileInfo> CleanupByAge(List<FileInfo> files, DateTime cutoffDate)
        {
            List<FileInfo> remainingFiles = [];

            foreach (FileInfo file in files)
            {
                if (file.LastWriteTime < cutoffDate)
                {
                    _ = TryDeleteFile(file);
                }
                else
                {
                    remainingFiles.Add(file);
                }
            }

            return remainingFiles;
        }

        /// <summary>
        /// Löscht älteste Dateien bis die Gesamtgröße unter dem Limit liegt.
        /// </summary>
        /// <param name="files">Liste der Log-Dateien (nach Alter sortiert).</param>
        /// <param name="maxSize">Maximale Gesamtgröße in Bytes.</param>
        private static void CleanupBySize(List<FileInfo> files, long maxSize)
        {
            long totalSize = files.Sum(f => f.Length);

            foreach (FileInfo file in files)
            {
                if (totalSize <= maxSize)
                {
                    break;
                }

                long fileSize = file.Length;

                if (TryDeleteFile(file))
                {
                    totalSize -= fileSize;
                }
            }
        }

        /// <summary>
        /// Versucht eine Datei zu löschen.
        /// </summary>
        /// <param name="file">Die zu löschende Datei.</param>
        /// <returns>True wenn erfolgreich, sonst false.</returns>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cleanup darf bei einzelnen Lösch-Fehlern nicht abbrechen; jeder Fehler wird geloggt, restliche Dateien werden weiter bearbeitet.")]
        private static bool TryDeleteFile(FileInfo file)
        {
            try
            {
                file.Delete();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"LogCleanup: Konnte {file.Name} nicht löschen: {ex.Message}");
                return false;
            }
        }
    }
}
