using EchoPlay.Core.Replication;
using EchoPlay.Core.Replication.Exchange;
using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Entities.Settings;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Replication.Exchange
{
    /// <summary>
    /// Der abgeglichene Bestand dieses Geräts als Änderungssätze, mit dem Weg zurück zur Entität.
    /// </summary>
    /// <remarks>
    /// Geladen wird alles, gelöschte Zeilen eingeschlossen: Ein Grabstein ist ein Stand wie jeder
    /// andere. Gibt es zu einem Schlüssel mehrere Zeilen, gilt die aktive, sonst die erste; die übrigen
    /// bleiben unberührt. Eine Zeile ohne Zeitpunkt (seit Einführung der Uhr nicht geändert) bekommt den
    /// kleinsten möglichen, damit jede echte Änderung gegen sie gewinnt.
    /// </remarks>
    internal sealed class LocalReplica
    {
        /// <summary>Zeitpunkt für Zeilen, die noch keinen haben; auf jedem Gerät derselbe.</summary>
        public static readonly HybridTimestamp Unstamped = new(0, 0, Guid.Empty);

        private readonly Dictionary<(string Kind, string Key), (BaseEntity? Entity, ChangeRecord Record)> _entries = [];
        private readonly Dictionary<string, List<DashboardPosition>> _positions = new(StringComparer.Ordinal);

        private LocalReplica()
        {
        }

        /// <summary>Alle Sätze des Bestands.</summary>
        public IEnumerable<ChangeRecord> Records => _entries.Values.Select(e => e.Record);

        /// <summary>Die Zeilen der Startseiten-Reihenfolge je Abschnitt.</summary>
        public IReadOnlyDictionary<string, List<DashboardPosition>> Positions => _positions;

        /// <summary>Lädt den Bestand.</summary>
        /// <param name="context">Der Datenbankkontext; die Entitäten werden darin verfolgt.</param>
        /// <param name="cancellationToken">Bricht das Laden ab.</param>
        /// <returns>Der Bestand.</returns>
        public static async Task<LocalReplica> LoadAsync(EchoPlayDbContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            LocalReplica replica = new();

            Dictionary<Guid, Series> seriesById = await replica.AddSeriesTreeAsync(context, cancellationToken).ConfigureAwait(false);
            await replica.AddStandaloneRowsAsync(context, cancellationToken).ConfigureAwait(false);

            List<DashboardPosition> positions = await context.DashboardPositions.AsTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
            replica.AddDashboardOrders(positions, seriesById);
            return replica;
        }

        /// <summary>
        /// Nimmt Serien, ihre Folgen und deren Wiedergabestände auf; Kinder ohne Elternzeile
        /// bleiben draußen, weil ihr Schlüssel aus dem der Eltern entsteht.
        /// </summary>
        /// <returns>Die Serien nach Kennung, für die Startseiten-Reihenfolge.</returns>
        private async Task<Dictionary<Guid, Series>> AddSeriesTreeAsync(EchoPlayDbContext context, CancellationToken cancellationToken)
        {
            List<Series> series = await context.Series.IgnoreQueryFilters().AsTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
            Dictionary<Guid, Series> seriesById = series.ToDictionary(s => s.Id);
            foreach (Series item in series)
            {
                Add(item, ChangeKinds.Series, SeriesKey(item), parent: null);
            }

            List<Episode> episodes = await context.Episodes.IgnoreQueryFilters().AsTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
            Dictionary<Guid, string> episodeKeys = [];
            foreach (Episode episode in episodes.Where(e => seriesById.ContainsKey(e.SeriesId)))
            {
                Series parent = seriesById[episode.SeriesId];
                string key = EpisodeKey(SeriesKey(parent), parent, episode);
                episodeKeys[episode.Id] = key;
                Add(episode, ChangeKinds.Episode, key, SeriesKey(parent));
            }

            List<PlaybackState> states = await context.PlaybackStates.IgnoreQueryFilters().AsTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (PlaybackState state in states.Where(s => episodeKeys.ContainsKey(s.EpisodeId)))
            {
                Add(state, ChangeKinds.PlaybackState, episodeKeys[state.EpisodeId], episodeKeys[state.EpisodeId]);
            }

            return seriesById;
        }

        /// <summary>Nimmt die Zeilen auf, die an keiner Serie hängen.</summary>
        private async Task AddStandaloneRowsAsync(EchoPlayDbContext context, CancellationToken cancellationToken)
        {
            foreach (WatchedTitle title in await context.WatchedTitles.IgnoreQueryFilters().AsTracking().ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                Add(title, ChangeKinds.WatchedTitle, title.NormalizedTitle, parent: null);
            }

            foreach (DialogSuppression suppression in await context.DialogSuppressions.IgnoreQueryFilters().AsTracking().ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                Add(suppression, ChangeKinds.DialogSuppression, suppression.Key, parent: null);
            }

            AppSettings? settings = await context.AppSettings.AsTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (settings is not null)
            {
                Add(settings, ChangeKinds.Settings, ChangeKinds.SettingsKey, parent: null, deleted: false);
            }
        }

        /// <summary>Der Schlüssel einer Serie.</summary>
        /// <param name="series">Die Serie.</param>
        /// <returns>Der fachliche Schlüssel.</returns>
        public static string SeriesKey(Series series)
            => BusinessKey.ForSeries(series.IsOnlineImported, series.AppleMusicArtistId, series.SpotifyArtistId, series.Title);

        /// <summary>Der Schlüssel einer Folge.</summary>
        /// <param name="seriesKey">Schlüssel der Serie.</param>
        /// <param name="series">Die Serie.</param>
        /// <param name="episode">Die Folge.</param>
        /// <returns>Der fachliche Schlüssel.</returns>
        public static string EpisodeKey(string seriesKey, Series series, Episode episode)
            => BusinessKey.ForEpisode(seriesKey, series.IsOnlineImported, episode.AppleMusicAlbumId, episode.SpotifyAlbumId, episode.EpisodeNumber, episode.Title);

        /// <summary>Sucht den Stand zu Art und Schlüssel.</summary>
        /// <param name="kind">Die Art.</param>
        /// <param name="key">Der Schlüssel.</param>
        /// <param name="entity">Die Entität, bei der Startseiten-Reihenfolge leer.</param>
        /// <param name="record">Der Stand.</param>
        /// <returns><see langword="true"/>, wenn es ihn gibt.</returns>
        public bool TryGet(string kind, string key, out BaseEntity? entity, out ChangeRecord? record)
        {
            bool found = _entries.TryGetValue((kind, key), out (BaseEntity? Entity, ChangeRecord Record) entry);
            entity = entry.Entity;
            record = found ? entry.Record : null;
            return found;
        }

        /// <summary>Sucht eine Serie nach Schlüssel.</summary>
        /// <param name="key">Der Schlüssel.</param>
        /// <returns>Die Serie, oder <see langword="null"/>.</returns>
        public Series? FindSeries(string? key)
            => key is not null && TryGet(ChangeKinds.Series, key, out BaseEntity? entity, out _) ? entity as Series : null;

        /// <summary>Sucht eine Folge nach Schlüssel.</summary>
        /// <param name="key">Der Schlüssel.</param>
        /// <returns>Die Folge, oder <see langword="null"/>.</returns>
        public Episode? FindEpisode(string? key)
            => key is not null && TryGet(ChangeKinds.Episode, key, out BaseEntity? entity, out _) ? entity as Episode : null;

        /// <summary>Merkt den neuen Stand eines übernommenen Satzes, damit Spätere ihn finden.</summary>
        /// <param name="entity">Die Entität, bei der Startseiten-Reihenfolge leer.</param>
        /// <param name="record">Der neue Stand.</param>
        public void Remember(BaseEntity? entity, ChangeRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            _entries[(record.Kind, record.Key)] = (entity, record);
        }

        private void Add(BaseEntity entity, string kind, string key, string? parent, bool? deleted = null)
        {
            ChangeRecord record = new(kind, key, parent, StampOf(entity.ChangeStamp), deleted ?? entity.IsDeleted, SharedFields.Read(entity));

            // Bei mehreren Zeilen zu einem Schlüssel gewinnt die aktive.
            if (_entries.TryGetValue((kind, key), out (BaseEntity? Entity, ChangeRecord Record) existing)
                && (existing.Entity is null || !existing.Entity.IsDeleted || entity.IsDeleted))
            {
                return;
            }

            _entries[(kind, key)] = (entity, record);
        }

        /// <summary>
        /// Je Abschnitt ein Satz mit den Serien-Schlüsseln in Reihenfolge. Der Zeitpunkt ist der jüngste
        /// der Zeilen: Die Liste wird immer ganz neu geschrieben, alle Zeilen tragen eine Änderung.
        /// </summary>
        private void AddDashboardOrders(List<DashboardPosition> positions, Dictionary<Guid, Series> seriesById)
        {
            foreach (IGrouping<string, DashboardPosition> section in positions.GroupBy(p => p.Section, StringComparer.Ordinal))
            {
                List<DashboardPosition> rows = [.. section.OrderBy(p => p.Position)];
                _positions[section.Key] = rows;
                string order = string.Join('\n', rows.Where(r => seriesById.ContainsKey(r.SeriesId)).Select(r => SeriesKey(seriesById[r.SeriesId])));
                HybridTimestamp stamp = rows.Select(r => StampOf(r.ChangeStamp)).Max();
                _entries[(ChangeKinds.DashboardOrder, section.Key)] = (null, new ChangeRecord(
                    ChangeKinds.DashboardOrder, section.Key, null, stamp, false,
                    new Dictionary<string, string?>(StringComparer.Ordinal) { [ChangeKinds.OrderField] = order }));
            }
        }

        private static HybridTimestamp StampOf(string? text)
            => HybridTimestamp.TryParse(text, out HybridTimestamp stamp) ? stamp : Unstamped;
    }
}
