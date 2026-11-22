using EchoPlay.LocalLibrary.Models;

namespace EchoPlay.LocalLibrary.Scanning
{
    /// <summary>
    /// Führt den Bibliotheks-Scan aus und meldet dabei, in welcher der vier Phasen er steht.
    /// Der <see cref="EchoPlay.LocalLibrary.Abstractions.ILocalLibraryScanner"/> selbst kennt
    /// keine Phasen, nur Zähler.
    /// </summary>
    public interface IScanOrchestrator
    {
        /// <summary>
        /// Führt den Scan durch und meldet den Phasen-Fortschritt über
        /// <paramref name="progress"/>.
        /// <para>
        /// Phase 1 (Vorbereitung) und Phase 2 (Serien erkennen) stehen am Anfang, solange der
        /// Scanner die Audiodateien zählt; der Balken ist dabei unbestimmt.<br/>
        /// Phase 4 (Tracks scannen) folgt mit Prozentangabe, sobald die Gesamtzahl bekannt ist.<br/>
        /// Phase 3 (Folgen ermitteln) erscheint statt Phase 4, wenn die Zählung scheiterte.
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
