using EchoPlay.LocalLibrary.Models;

namespace EchoPlay.LocalLibrary.Abstractions
{
    /// <summary>
    /// Analysiert und führt den Umbau einer flachen Dateistruktur in die
    /// Standard-Ordnerstruktur (ein Unterordner pro Folge) durch.
    /// </summary>
    public interface IFolderRestructureService
    {
        /// <summary>
        /// Analysiert den Serienordner und erstellt eine Vorschau der geplanten Verschiebungen.
        /// Auf dem Dateisystem ändert sich dabei nichts.
        /// </summary>
        /// <param name="seriesFolderPath">Absoluter Pfad zum Serienordner.</param>
        /// <param name="folderPattern">
        /// Das eingestellte Ordnermuster, etwa <c>"{number:000} - {title}"</c>. Es dient als
        /// erstes Muster beim Lesen von Nummer und Titel aus den Dateinamen. Die neuen Ordner
        /// heißen unabhängig davon <c>001 - Titel</c>; ohne erkannte Nummer nur nach dem Titel.
        /// </param>
        /// <returns>
        /// Vorschau mit allen geplanten Aktionen; leer, wenn der Ordner fehlt, nicht lesbar ist
        /// oder keine Audiodatei direkt enthält.
        /// </returns>
        RestructurePreview Analyze(string seriesFolderPath, string folderPattern);

        /// <summary>
        /// Führt die geplanten Verschiebungen aus.
        /// Legt Ordner an und verschiebt die Audiodateien.
        /// Bei Fehler werden bereits verschobene Dateien zurückgeschoben (Rollback).
        /// </summary>
        /// <param name="preview">Die Vorschau aus <see cref="Analyze"/>.</param>
        /// <returns>Anzahl der erfolgreich verschobenen Dateien.</returns>
        int Execute(RestructurePreview preview);

        /// <summary>
        /// Spielt ein liegengebliebenes Journal eines abgebrochenen Umbaus zurück: Dateien, die
        /// schon verschoben waren, kommen an ihren alten Platz.
        /// </summary>
        /// <param name="seriesFolderPath">Der Serienordner, in dem das Journal liegt.</param>
        /// <returns>Zahl der zurückverschobenen Dateien; 0, wenn kein Journal da ist.</returns>
        int TryRecoverPendingJournal(string seriesFolderPath);
    }
}
