using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.LocalLibrary.Scanning
{
    /// <summary>
    /// Implementierung von <see cref="IScanOrchestrator"/>.
    /// Koordiniert den vierphasigen Scan: Vorbereitung → Serien → Folgen → Tracks.
    /// Die Vorab-Zählung der Audiodateien in Phase 1 ermöglicht einen deterministischen
    /// Fortschrittsbalken in Phase 4, ohne den eigentlichen Scan zu verlangsamen.
    /// </summary>
    public sealed class ScanOrchestrator : IScanOrchestrator
    {
        private readonly ILocalLibraryScanner _scanner;

        // Unterstützte Audiodatei-Erweiterungen für die Vorab-Zählung in Phase 1
        private static readonly System.Collections.Generic.IReadOnlyList<string> AudioGlobPatterns =
            EchoPlay.Core.AudioExtensions.GlobPatterns;

        /// <summary>
        /// Initialisiert den Orchestrator mit dem zugrundeliegenden Dateisystem-Scanner.
        /// </summary>
        /// <param name="scanner">Der Scanner für die eigentliche Dateiverarbeitung.</param>
        public ScanOrchestrator(ILocalLibraryScanner scanner)
        {
            _scanner = scanner;
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<LocalScanResult>> ScanAsync(
            string rootPath,
            string episodeFolderPattern,
            IProgress<ScanProgress>? progress  = null,
            CancellationToken cancellationToken = default)
        {
            // ── Phase 1: Vorbereitung ──────────────────────────────────────────────
            // Audiodateien vorab zählen, damit Phase 4 einen deterministischen Fortschritt liefert.
            // Indeterministischer Balken solange die Zählung läuft.
            progress?.Report(new ScanProgress
            {
                Phase      = 1,
                PhaseLabel = ScanPhaseLabels.Preparation,
                StatusText = ScanPhaseLabels.Preparation
            });

            int totalFiles = await Task.Run(
                () => CountAudioFiles(rootPath),
                cancellationToken).ConfigureAwait(false);

            // ── Phasen 2–4: Scanner-Delegation ────────────────────────────────────
            // Der ILocalLibraryScanner übernimmt Serien-, Episoden- und Track-Erkennung.
            // Fortschritts-Events werden mit Phasen-Metadaten angereichert.
            IProgress<ScanProgress>? wrappedProgress = null;

            if (progress is not null)
            {
                // Phase 2 sofort melden, bevor der Scanner startet
                progress.Report(new ScanProgress
                {
                    Phase      = 2,
                    PhaseLabel = ScanPhaseLabels.Series,
                    StatusText = ScanPhaseLabels.Series
                });

                wrappedProgress = new Progress<ScanProgress>(p =>
                {
                    // Phase anhand der verfügbaren Zähler ableiten:
                    // TotalFiles > 0 → Track-Scan läuft (Phase 4)
                    // TotalSeries > 0 → Episoden/Serien-Sync (Phase 3)
                    int phase = p.TotalFiles > 0 ? 4 : 3;

                    string label = phase switch
                    {
                        4 => ScanPhaseLabels.Tracks,
                        _ => ScanPhaseLabels.Episodes
                    };

                    progress.Report(new ScanProgress
                    {
                        Phase           = phase,
                        PhaseLabel      = label,
                        ProcessedFiles  = p.ProcessedFiles,
                        // Vorab-Zählung aus Phase 1 verwenden, sofern der Scanner keinen Wert liefert
                        TotalFiles      = p.TotalFiles > 0 ? p.TotalFiles : totalFiles,
                        ProcessedSeries = p.ProcessedSeries,
                        TotalSeries     = p.TotalSeries,
                        StatusText      = p.StatusText,
                        DetailText      = p.DetailText
                    });
                });
            }

            return await _scanner.ScanSeriesAsync(
                rootPath,
                episodeFolderPattern,
                wrappedProgress,
                onSeriesScanned: null,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Zählt alle unterstützten Audiodateien im Verzeichnisbaum.
        /// Wird in Phase 1 auf dem Threadpool ausgeführt um den UI-Thread nicht zu blockieren.
        /// Bei IO-Fehlern (z.B. fehlende Rechte) wird 0 zurückgegeben – Phase 4 läuft dann
        /// weiter mit indeterministischem Balken.
        /// </summary>
        /// <param name="rootPath">Absoluter Pfad zum Bibliotheks-Wurzelordner.</param>
        /// <returns>Anzahl der Audiodateien oder 0 bei Fehler.</returns>
        private static int CountAudioFiles(string rootPath)
        {
            if (!Directory.Exists(rootPath))
            {
                return 0;
            }

            int count = 0;

            try
            {
                foreach (string extension in AudioGlobPatterns)
                {
                    count += Directory.GetFiles(rootPath, extension, SearchOption.AllDirectories).Length;
                }
            }
            catch (IOException)
            {
                // IO-Fehler (inkl. PathTooLongException, DirectoryNotFoundException) – ohne Zählung läuft der Scan trotzdem
            }
            catch (UnauthorizedAccessException)
            {
                // Zugriffsrechte – ohne Zählung läuft der Scan trotzdem
            }
            catch (SecurityException)
            {
                // Zugriffsrechte – ohne Zählung läuft der Scan trotzdem
            }
            catch (ArgumentException)
            {
                // Ungültiger Pfad – ohne Zählung läuft der Scan trotzdem
            }

            return count;
        }
    }
}
