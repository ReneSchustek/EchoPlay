using EchoPlay.App.Models;
using EchoPlay.Logger.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// App-Service für den Log-Viewer. Kapselt Dateisystemzugriffe auf das Log-Verzeichnis
    /// sowie das Filtern und Formatieren der Live-Einträge aus dem MemorySink.
    /// Die Auswahl-State (aktuell gewählte Datei, Suchtext, Level) bleibt im ViewModel – der
    /// Coordinator bietet reine, testbare Operationen ohne eigenen Zustand.
    /// </summary>

    public interface ILogViewerCoordinator
    {
        /// <summary>
        /// Gibt an, ob überhaupt ein MemorySink verfügbar ist.
        /// Ohne MemorySink liefert <see cref="BuildFilteredLiveEntries"/> nur leere Listen zurück.
        /// </summary>
        bool IsLiveViewAvailable { get; }

        /// <summary>
        /// Liest alle <c>.log</c>-Dateien aus dem konfigurierten Log-Verzeichnis und gibt sie als
        /// Auswahl-Optionen zurück, absteigend nach Datum sortiert. An erster Stelle steht immer
        /// die Live-Option (<see cref="LogFileOption.FilePath"/> ist <see langword="null"/>).
        /// </summary>
        /// <returns>Liste der Auswahl-Optionen.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<IReadOnlyList<LogFileOption>> LoadLogFileOptionsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Liest den Inhalt einer Log-Datei zeilenweise ein.
        /// Gesperrte oder nicht lesbare Dateien führen zu einer leeren Liste.
        /// </summary>
        /// <param name="filePath">Absoluter Pfad der Log-Datei.</param>
        /// <returns>Alle Zeilen der Datei.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<IReadOnlyList<string>> LoadFileLinesAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Baut die gefilterte Live-Ansicht aus dem MemorySink-Puffer.
        /// Einträge unterhalb <paramref name="minimumLevel"/> werden verworfen, ebenso Zeilen, die
        /// den <paramref name="searchText"/> (Groß-/Kleinschreibung ignoriert) nicht enthalten.
        /// </summary>
        /// <param name="searchText">Freitext-Filter. Leer → kein Textfilter.</param>
        /// <param name="minimumLevel">Mindest-Level – alles darunter wird übersprungen.</param>
        /// <returns>Formatierte, gefilterte Einträge in chronologischer Reihenfolge.</returns>
        IReadOnlyList<string> BuildFilteredLiveEntries(string searchText, LogLevel minimumLevel);
    }
}
