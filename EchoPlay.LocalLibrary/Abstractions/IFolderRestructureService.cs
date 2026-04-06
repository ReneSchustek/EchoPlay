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
        /// Verändert nichts auf dem Dateisystem – nur lesen.
        /// </summary>
        /// <param name="seriesFolderPath">Absoluter Pfad zum Serienordner.</param>
        /// <param name="folderPattern">
        /// Ordnermuster für die Zielbenennung, z.B. <c>"{number:000} - {title}"</c>.
        /// Bestimmt wie die neuen Unterordner benannt werden.
        /// </param>
        /// <returns>Vorschau mit allen geplanten Aktionen.</returns>
        RestructurePreview Analyze(string seriesFolderPath, string folderPattern);

        /// <summary>
        /// Führt die geplanten Verschiebungen aus.
        /// Legt Ordner an und verschiebt die Audiodateien.
        /// Bei Fehler werden bereits verschobene Dateien zurückgeschoben (Rollback).
        /// </summary>
        /// <param name="preview">Die Vorschau aus <see cref="Analyze"/>.</param>
        /// <returns>Anzahl der erfolgreich verschobenen Dateien.</returns>
        int Execute(RestructurePreview preview);
    }
}
