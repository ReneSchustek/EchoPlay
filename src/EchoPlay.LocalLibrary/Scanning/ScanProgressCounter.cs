using EchoPlay.LocalLibrary.Models;

namespace EchoPlay.LocalLibrary.Scanning
{
    /// <summary>
    /// Zählt die eingelesenen Audiodateien eines Scans mit und meldet nach jeder Folge den Stand.
    /// </summary>
    /// <param name="progress">Empfänger des Fortschritts; ohne ihn wird nur gezählt.</param>
    /// <param name="totalFiles">Die vorab gezählten Audiodateien; 0, wenn unbekannt.</param>
    internal sealed class ScanProgressCounter(IProgress<ScanProgress>? progress, int totalFiles)
    {
        private int _processedFiles;

        /// <summary>Zählt die Dateien einer Folge dazu und meldet den Stand.</summary>
        /// <param name="files">Die Zahl der Audiodateien dieser Folge.</param>
        /// <param name="seriesName">Die Serie, die gerade eingelesen wird.</param>
        public void Advance(int files, string seriesName)
        {
            _processedFiles += files;
            progress?.Report(new ScanProgress
            {
                ProcessedFiles = _processedFiles,
                TotalFiles = totalFiles,
                StatusText = $"Scanne {seriesName} …"
            });
        }
    }
}
