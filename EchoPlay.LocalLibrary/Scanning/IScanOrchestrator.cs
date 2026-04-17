using EchoPlay.LocalLibrary.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.LocalLibrary.Scanning
{
    /// <summary>
    /// Koordiniert den vierphasigen Bibliotheks-Scan und meldet Phasen-Fortschritt.
    /// Im Gegensatz zum <see cref="EchoPlay.LocalLibrary.Abstractions.ILocalLibraryScanner"/>
    /// zählt der Orchestrator die Gesamtzahl der Audiodateien in Phase 1 vor,
    /// damit der Fortschrittsbalken in Phase 4 deterministisch ist.
    /// </summary>
    public interface IScanOrchestrator
    {
        /// <summary>
        /// Führt den vierphasigen Scan durch und meldet Phasen-Fortschritt über den
        /// <paramref name="progress"/>-Parameter.
        /// <para>
        /// Phase 1 – Vorbereitung: Audiodateien zählen (Fortschrittsbalken indeterministisch).<br/>
        /// Phase 2 – Serien erkennen: Ordnerstruktur ermitteln.<br/>
        /// Phase 3 – Folgen ermitteln: Episodenordner pro Serie scannen.<br/>
        /// Phase 4 – Tracks scannen: ID3-Metadaten lesen (deterministischer Fortschritt).
        /// </para>
        /// </summary>
        /// <param name="rootPath">Absoluter Pfad zum Wurzelverzeichnis der Bibliothek.</param>
        /// <param name="episodeFolderPattern">
        /// Muster für die Erkennung von Episodenordnern (z.B. <c>"{number} - {title}"</c>).
        /// </param>
        /// <param name="progress">
        /// Optionaler Fortschritts-Callback mit <see cref="ScanProgress"/>-Objekten,
        /// die Phasennummer (<see cref="ScanProgress.Phase"/>) und Phasenbeschreibung
        /// (<see cref="ScanProgress.PhaseLabel"/>) enthalten.
        /// </param>
        /// <param name="cancellationToken">Token zum Abbrechen des Scans.</param>
        /// <returns>
        /// Liste aller gescannten Serien mit Episoden und Track-Pfaden.
        /// Gibt eine leere Liste zurück wenn das Verzeichnis nicht existiert.
        /// </returns>
        Task<IReadOnlyList<LocalScanResult>> ScanAsync(
            string rootPath,
            string episodeFolderPattern,
            IProgress<ScanProgress>? progress = null,
            CancellationToken cancellationToken = default);
    }
}
