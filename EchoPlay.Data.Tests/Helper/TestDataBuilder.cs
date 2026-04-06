using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;

namespace EchoPlay.Data.Tests.Helper
{
    /// <summary>
    /// Stellt explizite Hilfsmethoden zum Erzeugen und Persistieren konsistenter Testdaten für EF-Core-Integrationstests bereit. 
    /// Der Fokus liegt auf Korrektheit, Lesbarkeit und realistischem Datenbankverhalten statt auf maximaler Bequemlichkeit.
    /// </summary>
    /// <remarks>
    /// Initialisiert eine neue Instanz des <see cref="TestDataBuilder"/>.
    /// </remarks>
    /// <param name="context">Der zu verwendende Datenbankkontext, der bereits mit einem relationalen Provider wie SQLite konfiguriert sein muss.</param>
    public sealed class TestDataBuilder(EchoPlayDbContext context)
    {
        private readonly EchoPlayDbContext _context = context;

        /// <summary>
        /// Erstellt und persistiert eine neue Serie. Diese Methode stellt den Einstiegspunkt in den Aggregatbaum dar und muss immer vor abhängigen
        /// Entitäten wie Episoden aufgerufen werden, um Foreign-Key-Constraints korrekt einzuhalten.
        /// </summary>
        /// <param name="title">Der Anzeigename der Serie.</param>
        /// <returns>Die persistierte Serieninstanz inklusive generierter ID.</returns>
        public async Task<Series> PersistSeriesAsync(string title)
        {
            Series series = new()
            {
                Title = title
            };

            _context.Series.Add(series);
            await _context.SaveChangesAsync();

            return series;
        }

        /// <summary>
        /// Erstellt und persistiert eine Episode für eine bereits gespeicherte Serie. 
        /// Die Serie muss physisch in der Datenbank vorhanden sein, da SQLite Foreign-Key-Beziehungen unmittelbar beim Insert überprüft.
        /// </summary>
        /// <param name="series">Die bereits persistierte Elternserie.</param>
        /// <param name="title">Der Anzeigename der Episode.</param>
        /// <returns>Die persistierte Episodeninstanz.</returns>
        public async Task<Episode> PersistEpisodeAsync(Series series, string title)
        {
            Episode episode = new()
            {
                SeriesId = series.Id,
                Title = title
            };

            _context.Episodes.Add(episode);
            await _context.SaveChangesAsync();

            return episode;
        }

        /// <summary>
        /// Erstellt und persistiert einen PlaybackState für eine bereits gespeicherte Episode. 
        /// Der PlaybackState besitzt keine eigene fachliche Identität, sondern existiert ausschließlich als technisches Kindelement der Episode.
        /// </summary>
        /// <param name="episode">Die bereits persistierte Episode.</param>
        /// <returns>Der persistierte PlaybackState.</returns>
        public async Task<PlaybackState> PersistPlaybackStateAsync(Episode episode)
        {
            PlaybackState playbackState = new()
            {
                EpisodeId = episode.Id,
                LastPosition = TimeSpan.Zero
            };

            _context.PlaybackStates.Add(playbackState);
            await _context.SaveChangesAsync();

            return playbackState;
        }
    }
}