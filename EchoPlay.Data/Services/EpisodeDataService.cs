using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Internal;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.Data.Services
{
    /// <summary>
    /// EF-Core-basierte Implementierung des Datenservices für Episoden.
    /// Diese Klasse ist für den technischen Zugriff auf Episodendaten sowie für die Durchsetzung konsistenter Soft-Delete-Regeln verantwortlich.
    /// </summary>
    /// <remarks>Initialisiert eine neue Instanz des <see cref="EpisodeDataService"/>.</remarks>
    /// <param name="context">Der zu verwendende EF-Core-Datenbankkontext.</param>
    /// <param name="loggerFactory">Die Logger-Factory zur Erstellung des Loggers.</param>
    public sealed class EpisodeDataService(
        EchoPlayDbContext context,
        EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory) : IEpisodeDataService
    {
        private readonly EchoPlayDbContext _context = context;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger = loggerFactory.CreateLogger("EpisodeDataService");

        /// <inheritdoc/>
        public async Task<IReadOnlyList<Episode>> GetBySeriesIdsAsync(IReadOnlyList<Guid> seriesIds)
        {
            ArgumentNullException.ThrowIfNull(seriesIds);

            // Ein einziger Query mit WHERE SeriesId IN (...) statt N einzelne Abfragen.
            // Wichtig für die StatusBar-Statistiken, die alle abonnierten Serien umfassen.
            HashSet<Guid> idSet = new(seriesIds);

            List<Episode> result = await _context.Episodes

                .Where(episode => idSet.Contains(episode.SeriesId))
                .ToListAsync().ConfigureAwait(false);

            _logger.Debug($"{result.Count} Episode(n) für {seriesIds.Count} Serien geladen (Batch).");
            return result;
        }

        /// <summary>
        /// Liefert alle nicht logisch gelöschten Episoden einer Serie sortiert nach Episodennummer und Titel.
        /// </summary>
        /// <param name="seriesId">Die eindeutige ID der Serie.</param>
        public async Task<IReadOnlyList<Episode>> GetBySeriesIdAsync(Guid seriesId)
        {
            _logger.Debug($"Lade Episoden für Serie '{seriesId}'.");

            List<Episode> result = await _context.Episodes

                .Where(episode => episode.SeriesId == seriesId)
                .OrderBy(episode => episode.EpisodeNumber)
                .ThenBy(episode => episode.Title)
                .ToListAsync().ConfigureAwait(false);

            _logger.Debug($"{result.Count} Episode(n) für Serie '{seriesId}' geladen.");

            return result;
        }

        /// <summary>
        /// Liefert Episodenzähler für mehrere Serien in einer einzigen gruppierten Datenbankabfrage.
        /// Vermeidet das N+1-Problem beim Laden der lokalen Mediathek: ohne diese Methode wäre
        /// eine DB-Abfrage pro Serie nötig, mit ihr ist es eine.
        /// </summary>
        /// <param name="seriesIds">IDs der Serien, für die Zähler abgefragt werden.</param>
        /// <returns>
        /// Dictionary von SeriesId auf Episodenzähler.
        /// Serien ohne Episoden fehlen im Dictionary; der Aufrufer behandelt fehlende Einträge als (0, 0).
        /// </returns>
        public async Task<IReadOnlyDictionary<Guid, (int Total, int Local)>> GetEpisodeCountsForSeriesAsync(
            IReadOnlyList<Guid> seriesIds)
        {
            ArgumentNullException.ThrowIfNull(seriesIds);

            // Leere Eingabe → leere Antwort; SQL-IN über leere Liste wäre ungültig
            if (seriesIds.Count == 0)
            {
                return new Dictionary<Guid, (int Total, int Local)>(0);
            }

            // Hilfsprojekt für den EF-Core-GroupBy – anonyme Typen benötigen kein Objekt-Mapping
            List<EpisodeCountRow> rows = await _context.Episodes

                .Where(e => seriesIds.Contains(e.SeriesId))
                .GroupBy(e => e.SeriesId)
                .Select(g => new EpisodeCountRow
                {
                    SeriesId = g.Key,
                    Total    = g.Count(),
                    // Zählt Episoden, für die der lokale Ordner bereits gescannt wurde
                    Local    = g.Count(e => e.LocalFolderPath != null)
                })
                .ToListAsync().ConfigureAwait(false);

            Dictionary<Guid, (int Total, int Local)> result = new(rows.Count);

            foreach (EpisodeCountRow row in rows)
            {
                result[row.SeriesId] = (row.Total, row.Local);
            }

            return result;
        }

        /// <summary>
        /// Hilfsdatensatz für die GroupBy-Projektion in <see cref="GetEpisodeCountsForSeriesAsync"/>.
        /// Nicht im Interface sichtbar – rein technisches Mapping-Artefakt.
        /// </summary>
        private sealed class EpisodeCountRow
        {
            public Guid SeriesId { get; set; }
            public int Total { get; set; }
            public int Local { get; set; }
        }

        /// <summary>
        /// Liefert alle Episoden einer Serie, für die noch kein lokaler Ordner gescannt wurde.
        /// Nützlich um zu prüfen, welche Online-Episoden lokal noch fehlen.
        /// </summary>
        /// <param name="seriesId">Die ID der betroffenen Serie.</param>
        /// <returns>
        /// Episoden ohne <c>LocalFolderPath</c>, sortiert nach Episodennummer und Titel.
        /// Leere Liste wenn alle Episoden lokal gefunden wurden.
        /// </returns>
        public async Task<IReadOnlyList<Episode>> GetMissingLocalEpisodesAsync(Guid seriesId)
        {
            _logger.Debug($"Lade fehlende lokale Episoden für Serie '{seriesId}'.");

            List<Episode> result = await _context.Episodes

                .Where(e => e.SeriesId == seriesId && e.LocalFolderPath == null)
                .OrderBy(e => e.EpisodeNumber)
                .ThenBy(e => e.Title)
                .ToListAsync().ConfigureAwait(false);

            _logger.Debug($"{result.Count} fehlende Episoden für Serie '{seriesId}' gefunden.");

            return result;
        }

        /// <summary>
        /// Liefert eine Episode anhand ihrer eindeutigen ID oder <c>null</c>, wenn sie nicht existiert oder logisch gelöscht wurde.
        /// Der Zugriff erfolgt über <see cref="DbSet{TEntity}.FindAsync(object[])"/>, da es sich um einen Primary-Key-Zugriff handelt.
        /// </summary>
        /// <param name="id">Die eindeutige Episoden-ID.</param>
        public async Task<Episode?> GetByIdAsync(Guid id)
        {
            _logger.Debug($"Lade Episode mit ID '{id}'.");
            return await _context.Episodes.FindAsync(id).ConfigureAwait(false);
        }

        /// <summary>
        /// Fügt eine neue Episode dauerhaft hinzu.
        /// </summary>
        /// <param name="episode">Die zu persistierende Episode.</param>
        public async Task AddAsync(Episode episode)
        {
            ArgumentNullException.ThrowIfNull(episode);

            _ = _context.Episodes.Add(episode);
            _ = await _context.SaveChangesAsync().ConfigureAwait(false);
            _logger.Info($"Episode '{episode.Title}' (ID: {episode.Id}) hinzugefügt.");
        }

        /// <inheritdoc/>
        public async Task AddRangeAsync(IReadOnlyList<Episode> episodes)
        {
            ArgumentNullException.ThrowIfNull(episodes);

            if (episodes.Count == 0)
            {
                return;
            }

            _context.Episodes.AddRange(episodes);
            _ = await _context.SaveChangesAsync().ConfigureAwait(false);
            _logger.Info($"{episodes.Count} Episoden in einem Batch-Insert hinzugefügt.");
        }

        /// <summary>
        /// Aktualisiert eine bestehende Episode.
        /// Es wird davon ausgegangen, dass die Entität bereits aus dem aktuellen DbContext stammt.
        /// </summary>
        /// <param name="episode">Die zu aktualisierende Episode.</param>
        public async Task UpdateAsync(Episode episode)
        {
            ArgumentNullException.ThrowIfNull(episode);

            _ = _context.Episodes.Update(episode);
            _ = await _context.SaveChangesAsync().ConfigureAwait(false);
            _logger.Info($"Episode '{episode.Title}' (ID: {episode.Id}) aktualisiert.");
        }

        /// <summary>
        /// Ermittelt die höchste Episodennummer aller lokal vorhandenen Episoden einer Serie.
        /// Episoden ohne Nummer (<c>EpisodeNumber == null</c>) werden ignoriert, da sie
        /// keinen verlässlichen Vergleichswert liefern.
        /// </summary>
        /// <param name="seriesId">Die eindeutige ID der Serie.</param>
        /// <returns>
        /// Die höchste Episodennummer mit zugeordnetem lokalen Ordner,
        /// oder <see langword="null"/> wenn keine passende Episode existiert.
        /// </returns>
        public async Task<int?> GetHighestLocalEpisodeNumberAsync(Guid seriesId)
        {
            // MAX über eine leere Menge liefert in SQL NULL – EF Core bildet das auf int? ab
            int? result = await _context.Episodes

                .Where(e => e.SeriesId == seriesId
                         && e.LocalFolderPath != null
                         && e.EpisodeNumber != null)
                .MaxAsync(e => (int?)e.EpisodeNumber).ConfigureAwait(false);

            _logger.Debug($"Höchste lokale Folgennummer für Serie '{seriesId}': {result?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "keine"}.");

            return result;
        }

        /// <summary>
        /// Setzt das lokal gespeicherte Cover einer Episode dauerhaft.
        /// Existiert die Episode nicht, wird der Aufruf mit einer Warnung ignoriert.
        /// </summary>
        /// <param name="episodeId">Die ID der Episode.</param>
        /// <param name="coverData">Rohe Bilddaten oder <see langword="null"/> zum Entfernen.</param>
        public async Task SetLocalCoverAsync(Guid episodeId, byte[]? coverData)
        {
            Episode? episode = await _context.Episodes
                .AsTracking()
                .FirstOrDefaultAsync(e => e.Id == episodeId).ConfigureAwait(false);

            if (episode is null)
            {
                _logger.Warning($"Episode '{episodeId}' nicht gefunden – Cover-Update übersprungen.");
                return;
            }

            episode.LocalCoverData = coverData;
            _ = await _context.SaveChangesAsync().ConfigureAwait(false);

            _logger.Info($"Cover für Episode '{episode.Title}' (ID: {episodeId}) {(coverData is null ? "gelöscht" : "gespeichert")}.");
        }

        /// <inheritdoc/>
        public async Task SetCoverLastCheckedAsync(Guid episodeId, DateTime checkedAt)
        {
            Episode? episode = await _context.Episodes
                .AsTracking()
                .FirstOrDefaultAsync(e => e.Id == episodeId).ConfigureAwait(false);

            if (episode is null)
            {
                _logger.Warning($"Episode '{episodeId}' nicht gefunden – CoverLastChecked-Update übersprungen.");
                return;
            }

            episode.CoverLastChecked = checkedAt;
            _ = await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Markiert eine Episode sowie alle zugehörigen PlaybackStates als logisch gelöscht.
        /// Diese Methode setzt die mittlere Regel der Soft-Delete-Matrix um:
        /// Eine gelöschte Episode darf keine aktiven Wiedergabestände behalten, während die übergeordnete Serie unverändert bestehen bleibt.
        /// </summary>
        /// <param name="id">Die eindeutige ID der zu löschenden Episode.</param>
        public async Task DeleteAsync(Guid id)
        {
            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope($"DB:Episode:Delete:{id}");

            Episode? episode = await _context.Episodes
                .AsTracking()
                .FirstOrDefaultAsync(e => e.Id == id).ConfigureAwait(false);

            if (episode == null)
            {
                // Wenn die Episode nicht existiert, ist kein Soft-Delete erforderlich.
                _logger.Warning($"Episode mit ID '{id}' nicht gefunden – Soft-Delete übersprungen.");
                return;
            }

            // Die Episode wird ausschließlich über die definierte Domänenoperation logisch gelöscht.
            episode.MarkAsDeleted(EntityClock.Current.UtcNow);

            List<PlaybackState> playbackStates =
                await _context.PlaybackStates
                    .AsTracking()
                    .Where(state => state.EpisodeId == id)
                    .ToListAsync().ConfigureAwait(false);

            foreach (PlaybackState playbackState in playbackStates)
            {
                // PlaybackStates besitzen keine eigenständige fachliche Existenz und müssen bei der Löschung ihrer Episode ebenfalls entfernt werden.
                playbackState.MarkAsDeleted(EntityClock.Current.UtcNow);
            }

            _ = await _context.SaveChangesAsync().ConfigureAwait(false);
            _logger.Info($"Episode '{episode.Title}' (ID: {id}) und {playbackStates.Count} PlaybackState(s) als gelöscht markiert.");
        }
    }
}
