using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Models;

namespace EchoPlay.LocalLibrary.Scanning
{
    /// <summary>
    /// Setzt den Phasen-Fortschritt über den Scan des <see cref="ILocalLibraryScanner"/>.
    /// Der Scanner meldet nur Zähler; erst hier werden daraus die vier Phasen der Anzeige.
    /// </summary>
    public sealed class ScanOrchestrator : IScanOrchestrator
    {
        private readonly ILocalLibraryScanner _scanner;

        /// <summary>
        /// Initialisiert den Orchestrator mit dem zugrundeliegenden Dateisystem-Scanner.
        /// </summary>
        /// <param name="scanner">Der Scanner für die eigentliche Dateiverarbeitung.</param>
        public ScanOrchestrator(ILocalLibraryScanner scanner)
        {
            ArgumentNullException.ThrowIfNull(scanner);
            _scanner = scanner;
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<LocalScanResult>> ScanAsync(
            string rootPath,
            string episodeFolderPattern,
            IProgress<ScanProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            IProgress<ScanProgress>? phasedProgress = null;

            if (progress is not null)
            {
                progress.Report(new ScanProgress
                {
                    Phase = 1,
                    PhaseLabel = ScanPhaseLabels.Preparation,
                    StatusText = ScanPhaseLabels.Preparation
                });
                progress.Report(new ScanProgress
                {
                    Phase = 2,
                    PhaseLabel = ScanPhaseLabels.Series,
                    StatusText = ScanPhaseLabels.Series
                });

                phasedProgress = new Progress<ScanProgress>(p => progress.Report(WithPhase(p)));
            }

            return await _scanner.ScanSeriesAsync(
                rootPath,
                episodeFolderPattern,
                phasedProgress,
                onSeriesScanned: null,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Ordnet eine Meldung des Scanners ihrer Phase zu. Kennt der Scanner die Gesamtzahl der
        /// Audiodateien, liest er Spuren (Phase 4); scheiterte seine Vorab-Zählung, bleibt der
        /// Balken unbestimmt und die Anzeige bei „Folgen werden ermittelt".
        /// </summary>
        private static ScanProgress WithPhase(ScanProgress scannerProgress)
        {
            bool tracksKnown = scannerProgress.TotalFiles > 0;

            return new ScanProgress
            {
                Phase = tracksKnown ? 4 : 3,
                PhaseLabel = tracksKnown ? ScanPhaseLabels.Tracks : ScanPhaseLabels.Episodes,
                ProcessedFiles = scannerProgress.ProcessedFiles,
                TotalFiles = scannerProgress.TotalFiles,
                ProcessedSeries = scannerProgress.ProcessedSeries,
                TotalSeries = scannerProgress.TotalSeries,
                StatusText = scannerProgress.StatusText,
                DetailText = scannerProgress.DetailText
            };
        }
    }
}
