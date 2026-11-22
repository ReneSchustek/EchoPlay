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

            // Beide Sink-Dateitypen berücksichtigen: FileSink (.log) und JsonLogSink (.jsonl),
            // auch wenn die JSON-Dateien in einem eigenen Ordner liegen. Sonst wüchsen sie
            // unbegrenzt, weil sie nie bereinigt würden.
            string[] directories = [.. new[] { _options.LogDirectory, _options.JsonLogDirectory }
                .Where(directory => !string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                .Select(directory => directory!)
                .Distinct(StringComparer.OrdinalIgnoreCase)];
            if (directories.Length == 0)
            {
                return;
            }

            string[] logPatterns = ["*.log", "*.jsonl"];

            List<FileInfo> logFiles = [.. directories
                .SelectMany(directory => logPatterns.SelectMany(pattern => Directory.GetFiles(directory, pattern)))
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.LastWriteTime)];

            // Bereinigen nach Alter
            if (_options.RetentionDays > 0)
            {
                // Lokalzeit, weil Log-Dateien lokale Zeitstempel im Namen tragen; UTC wäre
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
        private static bool TryDeleteFile(FileInfo file)
        {
            try
            {
                file.Delete();
                return true;
            }
            catch (IOException ex)
            {
                // Die Datei wird gerade beschrieben oder ist in einem anderen Programm geöffnet, etwa im
                // Editor; sie bleibt stehen und fällt beim nächsten Aufräumen weg.
                TraceDeleteFailure(file, ex);
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                TraceDeleteFailure(file, ex);
                return false;
            }
        }

        // Nur auf den Trace-Kanal: Der Logger selbst schreibt in genau diese Dateien.
        private static void TraceDeleteFailure(FileInfo file, Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"LogCleanup: Konnte {file.Name} nicht löschen: {ex.Message}");
        }
    }
}
