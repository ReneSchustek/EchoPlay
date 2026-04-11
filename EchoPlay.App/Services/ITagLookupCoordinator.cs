using EchoPlay.TagManager.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// App-Service, der den Online-Tag-Lookup für den Tag-Manager kapselt.
    /// Enthält das Bauen der Suchanfrage aus einem Ordnerkontext, den eigentlichen
    /// MusicBrainz-Aufruf (über <see cref="EchoPlay.TagManager.Abstractions.ITagLookupService"/>)
    /// und die Auswahl des besten Treffers anhand der Track-Anzahl. So bleibt der
    /// Tag-Editor-Zustand frei von Lookup-Logik und ist unabhängig testbar.
    /// </summary>
    public interface ITagLookupCoordinator
    {
        /// <summary>
        /// Führt eine MusicBrainz-Suche mit dem übergebenen Freitext aus.
        /// </summary>
        /// <param name="query">Freitext-Suchanfrage, z.B. <c>"Die drei ??? Der Super-Papagei"</c>.</param>
        /// <param name="cancellationToken">Token zum Abbrechen der Anfrage.</param>
        /// <returns>Relevanzsortierte Ergebnisliste, leer wenn keine Treffer.</returns>
        Task<IReadOnlyList<TagLookupResult>> SearchAsync(string query, CancellationToken cancellationToken = default);

        /// <summary>
        /// Baut die Suchanfrage aus dem Ordnerkontext einer geöffneten Episode zusammen.
        /// Serienname = übergeordneter Ordner, Folgentitel = aktueller Ordner ohne führende Laufnummer.
        /// </summary>
        /// <param name="folderPath">Absoluter Pfad zum geöffneten Episodenordner. Darf <see langword="null"/> sein.</param>
        /// <returns>Kombinierter Suchbegriff oder ein leerer String wenn kein Ordnerkontext vorliegt.</returns>
        string BuildAutoLookupQuery(string? folderPath);

        /// <summary>
        /// Wählt den besten Treffer aus einer Ergebnisliste anhand der Track-Anzahl.
        /// Ein exakter Treffer (TrackCount stimmt mit <paramref name="loadedTrackCount"/> überein)
        /// hat höchste Priorität; andernfalls wird der erste Treffer zurückgegeben.
        /// </summary>
        /// <param name="results">Die von <see cref="SearchAsync"/> gelieferten Ergebnisse.</param>
        /// <param name="loadedTrackCount">Anzahl der aktuell im Tag-Manager geladenen Tracks.</param>
        /// <returns>Der beste Treffer oder <see langword="null"/> bei leerer Liste.</returns>
        TagLookupResult? SelectBestMatch(IReadOnlyList<TagLookupResult> results, int loadedTrackCount);
    }
}
