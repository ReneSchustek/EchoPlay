using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Scanning;

namespace EchoPlay.LocalLibrary.Abstractions
{
    /// <summary>
    /// Definiert den Vertrag für das Scannen einer lokalen Hörspielbibliothek.
    /// </summary>
    public interface ILocalLibraryScanner
    {
        /// <summary>
        /// Gibt alle direkten Unterordner des Wurzelverzeichnisses zurück.
        /// Sehr schnell – nur <c>Directory.GetDirectories</c>, kein Dateisystem-Scan tiefer.
        /// Ermöglicht dem SyncService eine sofortige Vorab-Anzeige aller erkannten Serienordner.
        /// Bei IO-Fehlern wird eine leere Liste zurückgegeben.
        /// </summary>
        /// <param name="rootPath">Absoluter Pfad zum Wurzelverzeichnis der Bibliothek.</param>
        /// <returns>Absoluter Pfad jedes direkten Unterordners, oder leere Liste bei Fehler.</returns>
        IReadOnlyList<string> GetSeriesFolders(string rootPath);
        /// <summary>
        /// Durchsucht das angegebene Wurzelverzeichnis nach Hörspielserien und Episoden.
        /// Die Episodenordner werden anhand des übergebenen Musters erkannt und geparst.
        /// </summary>
        /// <param name="rootPath">Absoluter Pfad zum Wurzelverzeichnis der Bibliothek.</param>
        /// <param name="folderPattern">
        /// Muster für Episodenordner, z.B. <c>"{number:000} - {title}"</c>.
        /// Unterstützte Platzhalter: <c>{number}</c>, <c>{number:000}</c>, <c>{title}</c>.
        /// </param>
        /// <param name="progress">
        /// Optionaler Fortschritts-Callback – erhält <see cref="ScanProgress"/> nach jeder
        /// verarbeiteten Episode. Null wenn kein Fortschritt gemeldet werden soll.
        /// </param>
        /// <param name="onSeriesScanned">
        /// Optionaler Callback, der nach jeder vollständig gescannten Serie aufgerufen wird.
        /// Ermöglicht es dem Aufrufer, Serien sofort zu verarbeiten, ohne auf das Gesamtergebnis
        /// zu warten – Grundlage für die progressive Anzeige in der UI.
        /// </param>
        /// <param name="ct">Abbruchtoken – ermöglicht sauberes Beenden bei App-Schließen.</param>
        /// <returns>
        /// Liste aller gefundenen Serien mit ihren Episoden.
        /// Gibt eine leere Liste zurück, wenn das Verzeichnis nicht existiert oder leer ist.
        /// </returns>
        Task<IReadOnlyList<LocalScanResult>> ScanSeriesAsync(
            string rootPath,
            string folderPattern,
            IProgress<ScanProgress>? progress = null,
            IProgress<LocalScanResult>? onSeriesScanned = null,
            CancellationToken ct = default);
    }
}
