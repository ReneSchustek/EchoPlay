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

        // Compiled Query für die Hot-Aggregation: Expression-Tree wird einmal beim Static-Init
        // zu SQL übersetzt und für alle weiteren Aufrufe wiederverwendet. Spart pro Aufruf den
        // Translation-Overhead, den EF Core sonst je Dashboard-Refresh × Serien-Anzahl bezahlt.
        // Globale Soft-Delete-Filter werden auch in CompileAsyncQuery angewendet (EF Core 10).
        private static readonly Func<EchoPlayDbContext, IReadOnlyList<Guid>, IAsyncEnumerable<EpisodeCountRow>>
            GetEpisodeCountsCompiled = EF.CompileAsyncQuery(
                (EchoPlayDbContext ctx, IReadOnlyList<Guid> ids) =>
                    ctx.Episodes
                       .Where(episode => ids.Contains(episode.SeriesId))
                       .GroupBy(episode => episode.SeriesId)
                       .Select(group => new EpisodeCountRow
                       {
                           SeriesId = group.Key,
                           Total = group.Count(),
                           Local = group.Count(episode => episode.LocalFolderPath != null)
                       }));

        /// <inheritdoc/>
        /// <param name="seriesIds">Parameter seriesIds.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<IReadOnlyList<Episode>> GetBySeriesIdsAsync(IReadOnlyList<Guid> seriesIds, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(seriesIds);

            // Ein einziger Query mit WHERE SeriesId IN (...) statt N einzelne Abfragen.
            // Wichtig für die StatusBar-Statistiken, die alle abonnierten Serien umfassen.
            HashSet<Guid> idSet = new(seriesIds);

            List<Episode> result = await _context.Episodes

                .Where(episode => idSet.Contains(episode.SeriesId))
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            _logger.Debug(() => $"{result.Count} Episode(n) für {seriesIds.Count} Serien geladen (Batch).");
            return result;
        }

        /// <summary>
        /// Liefert alle nicht logisch gelöschten Episoden einer Serie sortiert nach Episodennummer und Titel.
        /// </summary>
        /// <param name="seriesId">Die eindeutige ID der Serie.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<IReadOnlyList<Episode>> GetBySeriesIdAsync(Guid seriesId, CancellationToken cancellationToken = default)
        {
            _logger.Debug(() => $"Lade Episoden für Serie '{seriesId}'.");

            List<Episode> result = await _context.Episodes

                .Where(episode => episode.SeriesId == seriesId)
                .OrderBy(episode => episode.EpisodeNumber)
                .ThenBy(episode => episode.Title)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            _logger.Debug(() => $"{result.Count} Episode(n) für Serie '{seriesId}' geladen.");

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
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<IReadOnlyDictionary<Guid, (int Total, int Local)>> GetEpisodeCountsForSeriesAsync(
            IReadOnlyList<Guid> seriesIds, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(seriesIds);

            // Leere Eingabe → leere Antwort; SQL-IN über leere Liste wäre ungültig
            if (seriesIds.Count == 0)
            {
                return new Dictionary<Guid, (int Total, int Local)>(0);
            }

            // Compiled Query: Expression-Tree wurde einmalig beim Static-Init übersetzt.
            // Materialisierung erfolgt streamend – Dictionary-Aufbau ohne Zwischen-List.
            Dictionary<Guid, (int Total, int Local)> result = [];

            await foreach (EpisodeCountRow row in GetEpisodeCountsCompiled(_context, seriesIds).ConfigureAwait(false))
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
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<IReadOnlyList<Episode>> GetMissingLocalEpisodesAsync(Guid seriesId, CancellationToken cancellationToken = default)
        {
            _logger.Debug(() => $"Lade fehlende lokale Episoden für Serie '{seriesId}'.");

            List<Episode> result = await _context.Episodes

                .Where(e => e.SeriesId == seriesId && e.LocalFolderPath == null)
                .OrderBy(e => e.EpisodeNumber)
                .ThenBy(e => e.Title)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            _logger.Debug(() => $"{result.Count} fehlende Episoden für Serie '{seriesId}' gefunden.");

            return result;
        }

        /// <summary>
        /// Liefert eine Episode anhand ihrer eindeutigen ID oder <c>null</c>, wenn sie nicht existiert oder logisch gelöscht wurde.
        /// Der Zugriff erfolgt über <see cref="DbSet{TEntity}.FindAsync(object[])"/>, da es sich um einen Primary-Key-Zugriff handelt.
        /// </summary>
        /// <param name="id">Die eindeutige Episoden-ID.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<Episode?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _logger.Debug(() => $"Lade Episode mit ID '{id}'.");
            return await _context.Episodes.FindAsync([id], cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        /// <param name="ids">Parameter ids.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<IReadOnlyDictionary<Guid, Episode>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(ids);

            // Leere Eingabe → leeres Dictionary; ein SQL-IN über null Elemente wäre ungültig
            if (ids.Count == 0)
            {
                return new Dictionary<Guid, Episode>(0);
            }

            // Doppel-IDs ignorieren – das Set vermeidet redundante Parameter im IN-Filter
            HashSet<Guid> idSet = new(ids);

            List<Episode> rows = await _context.Episodes
                .Where(e => idSet.Contains(e.Id))
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            Dictionary<Guid, Episode> result = new(rows.Count);
            foreach (Episode episode in rows)
            {
                result[episode.Id] = episode;
            }

            _logger.Debug(() => $"{result.Count} von {idSet.Count} angeforderten Episoden im Batch geladen.");
            return result;
        }

        /// <summary>
        /// Fügt eine neue Episode dauerhaft hinzu.
        /// </summary>
        /// <param name="episode">Die zu persistierende Episode.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task AddAsync(Episode episode, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(episode);

            _ = _context.Episodes.Add(episode);
            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("Episode '{EpisodeTitle}' (ID: {EpisodeId}) hinzugefügt.", episode.Title, episode.Id);
        }

        /// <inheritdoc/>
        /// <param name="episodes">Parameter episodes.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task AddRangeAsync(IReadOnlyList<Episode> episodes, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(episodes);

            if (episodes.Count == 0)
            {
                return;
            }

            _context.Episodes.AddRange(episodes);
            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("{EpisodeCount} Episoden in einem Batch-Insert hinzugefügt.", episodes.Count);
        }

        /// <summary>
        /// Aktualisiert eine bestehende Episode.
        /// Es wird davon ausgegangen, dass die Entität bereits aus dem aktuellen DbContext stammt.
        /// </summary>
        /// <param name="episode">Die zu aktualisierende Episode.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task UpdateAsync(Episode episode, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(episode);

            _ = _context.Episodes.Update(episode);
            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("Episode '{EpisodeTitle}' (ID: {EpisodeId}) aktualisiert.", episode.Title, episode.Id);
        }

        /// <inheritdoc/>
        /// <param name="episodes">Parameter episodes.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task UpdateRangeAsync(IReadOnlyList<Episode> episodes, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(episodes);

            if (episodes.Count == 0)
            {
                return;
            }

            _context.Episodes.UpdateRange(episodes);
            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("{EpisodeCount} Episoden in einem Batch-Update aktualisiert.", episodes.Count);
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
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<int?> GetHighestLocalEpisodeNumberAsync(Guid seriesId, CancellationToken cancellationToken = default)
        {
            // MAX über eine leere Menge liefert in SQL NULL – EF Core bildet das auf int? ab
            int? result = await _context.Episodes

                .Where(e => e.SeriesId == seriesId
                         && e.LocalFolderPath != null
                         && e.EpisodeNumber != null)
                .MaxAsync(e => (int?)e.EpisodeNumber, cancellationToken).ConfigureAwait(false);

            _logger.Debug(() => $"Höchste lokale Folgennummer für Serie '{seriesId}': {result?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "keine"}.");

            return result;
        }

        /// <inheritdoc/>
        /// <param name="episodeId">Parameter episodeId.</param>
        /// <param name="checkedAt">Parameter checkedAt.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task SetCoverLastCheckedAsync(Guid episodeId, DateTime checkedAt, CancellationToken cancellationToken = default)
        {
            Episode? episode = await _context.Episodes
                .LoadTrackedByIdOrWarnAsync(_logger, episodeId, "Episode", "CoverLastChecked-Update", cancellationToken).ConfigureAwait(false);

            if (episode is null)
            {
                return;
            }

            episode.CoverLastChecked = checkedAt;
            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Markiert eine Episode sowie alle zugehörigen PlaybackStates als logisch gelöscht.
        /// Diese Methode setzt die mittlere Regel der Soft-Delete-Matrix um:
        /// Eine gelöschte Episode darf keine aktiven Wiedergabestände behalten, während die übergeordnete Serie unverändert bestehen bleibt.
        /// </summary>
        /// <param name="id">Die eindeutige ID der zu löschenden Episode.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope($"DB:Episode:Delete:{id}");

            Episode? episode = await _context.Episodes
                .LoadTrackedByIdOrWarnAsync(_logger, id, "Episode", "Soft-Delete", cancellationToken).ConfigureAwait(false);

            if (episode is null)
            {
                // Wenn die Episode nicht existiert, ist kein Soft-Delete erforderlich.
                return;
            }

            // Die Episode wird ausschließlich über die definierte Domänenoperation logisch gelöscht.
            episode.MarkAsDeleted(EntityClock.Current.UtcNow);

            List<PlaybackState> playbackStates =
                await _context.PlaybackStates
                    .AsTracking()
                    .Where(state => state.EpisodeId == id)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

            // PlaybackStates besitzen keine eigenständige fachliche Existenz und müssen bei der Löschung ihrer Episode ebenfalls entfernt werden.
            playbackStates.MarkRangeDeleted();

            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info(
                "Episode '{EpisodeTitle}' (ID: {EpisodeId}) und {PlaybackStateCount} PlaybackState(s) als gelöscht markiert.",
                episode.Title, id, playbackStates.Count);
        }
    }
}
