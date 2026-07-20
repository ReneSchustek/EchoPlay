using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Internal;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Data.Services.Projections;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EchoPlay.Data.Services
{
    /// <summary>
    /// EF-Core-basierte Implementierung des Datenservices für Wiedergabestände.
    /// Diese Klasse kapselt den technischen Zugriff auf PlaybackStates und stellt sicher, dass Soft-Delete-Regeln konsistent eingehalten werden.
    /// </summary>
    /// <remarks>Initialisiert eine neue Instanz des <see cref="PlaybackStateDataService"/>.</remarks>
    /// <param name="context">Der zu verwendende EF-Core-Datenbankkontext.</param>
    /// <param name="loggerFactory">Die Logger-Factory zur Erstellung des Loggers.</param>
    public sealed class PlaybackStateDataService(
        EchoPlayDbContext context,
        EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory) : IPlaybackStateDataService
    {
        private readonly EchoPlayDbContext _context = context;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger = loggerFactory.CreateLogger("PlaybackStateDataService");

        // Compiled Query für die "Zuletzt gehört"-Liste: einmaliger Translate-Schritt beim
        // Static-Init, danach reine Parameter-Substitution. Globale Soft-Delete-Filter werden
        // auch in CompileAsyncQuery angewendet (EF Core 10) – kein zusätzlicher WHERE-Klauselzwang.
        private static readonly Func<EchoPlayDbContext, int, IAsyncEnumerable<RecentPlaybackRow>>
            GetRecentActiveCompiled = EF.CompileAsyncQuery(
                (EchoPlayDbContext ctx, int maxRows) =>
                    ctx.PlaybackStates
                       .Where(state => state.IsCompleted || state.LastPosition != TimeSpan.Zero)
                       .OrderByDescending(state => state.LastPlayedAt ?? state.UpdatedAt ?? state.CreatedAt)
                       .Take(maxRows)
                       .Select(state => new RecentPlaybackRow(
                           state.Id,
                           state.EpisodeId,
                           state.IsCompleted,
                           state.LastPosition,
                           state.LastPlayedAt ?? state.UpdatedAt ?? state.CreatedAt)));

        /// <summary>
        /// Liefert alle aktiven (nicht gelöschten) Wiedergabestände als flache Liste.
        /// Der globale Query-Filter des DbContext schließt logisch gelöschte Einträge automatisch aus.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<IReadOnlyList<PlaybackState>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            _logger.Debug("Lade alle Wiedergabestände.");
            return await _context.PlaybackStates.ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Liefert den Wiedergabestatus zu einer Episode oder <c>null</c>, wenn kein entsprechender Eintrag existiert oder dieser logisch
        /// gelöscht wurde.
        /// Da die EpisodeId ein fachlicher Suchschlüssel ist, wird hier FirstOrDefaultAsync verwendet.
        /// </summary>
        /// <param name="episodeId">Die eindeutige ID der Episode.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<PlaybackState?> GetByEpisodeIdAsync(Guid episodeId, CancellationToken cancellationToken = default)
        {
            _logger.Debug(() => $"Lade PlaybackState für Episode '{episodeId}'.");
            return await _context.PlaybackStates
                .FirstOrDefaultAsync(state => state.EpisodeId == episodeId, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Fügt einen neuen Wiedergabestatus dauerhaft hinzu.
        /// </summary>
        /// <param name="playbackState">Der zu persistierende Wiedergabestatus.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task AddAsync(PlaybackState playbackState, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(playbackState);

            _ = _context.PlaybackStates.Add(playbackState);

            if (await _context.TrySaveChangesIgnoreUniqueAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                // Paralleler Scope hat bereits einen PlaybackState für dieselbe Episode angelegt.
                // Der erste Eintrag gewinnt — redundante Einfügung ignorieren.
                _logger.Warning("UNIQUE-Konflikt beim Anlegen von PlaybackState für Episode '{EpisodeId}' ignoriert.", playbackState.EpisodeId);
                return;
            }

            _logger.Info("PlaybackState (ID: {PlaybackStateId}) für Episode '{EpisodeId}' hinzugefügt.", playbackState.Id, playbackState.EpisodeId);
        }

        /// <summary>
        /// Aktualisiert einen bestehenden Wiedergabestatus.
        /// Es wird davon ausgegangen, dass die Entität bereits aus dem aktuellen DbContext stammt.
        /// </summary>
        /// <param name="playbackState">Der zu aktualisierende Wiedergabestatus.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task UpdateAsync(PlaybackState playbackState, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(playbackState);

            _ = _context.PlaybackStates.Update(playbackState);
            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("PlaybackState (ID: {PlaybackStateId}) für Episode '{EpisodeId}' aktualisiert.", playbackState.Id, playbackState.EpisodeId);
        }

        /// <summary>
        /// Berechnet aggregierte Wiedergabe-Zähler für alle Episoden einer Serie vollständig serverseitig.
        /// Ein einziges SELECT mit Left-Join Episode → PlaybackState liefert Total-, Finished- und
        /// InProgress-Counts ohne Entity-Materialisierung; die Soft-Delete-Filter beider Entitäten
        /// werden automatisch durch die globalen Query-Filter angewendet.
        /// </summary>
        /// <param name="seriesId">ID der Serie, deren Episoden ausgewertet werden.</param>
        /// <returns>
        /// Tuple (Finished, InProgress, NotStarted). Gibt (0, 0, 0) zurück,
        /// wenn für die Serie keine (aktiven) Episoden existieren.
        /// </returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<(int Finished, int InProgress, int NotStarted)> GetCountsBySeriesIdAsync(Guid seriesId, CancellationToken cancellationToken = default)
        {
            _logger.Debug(() => $"Lade aggregierte Wiedergabezähler für Serie '{seriesId}'.");

            // Left-Join Episode → PlaybackState mit DefaultIfEmpty; Episoden ohne State liefern p == null
            // und zählen damit in „Total", aber nicht in Finished/InProgress.
            // Equality-Vergleich auf TimeSpan.Zero ist serverseitig übersetzbar (TEXT-Repräsentation
            // '00:00:00' im SQLite-Schema), Ordering-Vergleiche wären es nicht.
            PlaybackCountRow? row = await _context.Episodes
                .Where(e => e.SeriesId == seriesId)
                .GroupJoin(
                    _context.PlaybackStates,
                    e => e.Id,
                    p => p.EpisodeId,
                    (e, ps) => new { Episode = e, States = ps })
                .SelectMany(x => x.States.DefaultIfEmpty(), (x, p) => new { p })
                .GroupBy(_ => 1)
                .Select(g => new PlaybackCountRow
                {
                    Total = g.Count(),
                    Finished = g.Count(x => x.p != null && x.p.IsCompleted),
                    InProgress = g.Count(x => x.p != null && !x.p.IsCompleted && x.p.LastPosition != TimeSpan.Zero)
                })
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (row is null)
            {
                return (0, 0, 0);
            }

            int notStarted = row.Total - row.Finished - row.InProgress;
            return (row.Finished, row.InProgress, notStarted);
        }

        /// <summary>
        /// Liefert die N zuletzt aktiven Wiedergabestände als schmale Projektion.
        /// Filterung („IsCompleted oder LastPosition &gt; 0") und Sortierung erfolgen serverseitig –
        /// in Verbindung mit dem Index auf <c>LastPlayedAt</c> ein Index-Scan + Limit statt Tablescan.
        /// </summary>
        /// <param name="maxRows">Maximale Anzahl Zeilen. Werte ≤ 0 liefern eine leere Liste.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<IReadOnlyList<RecentPlaybackRow>> GetRecentActiveAsync(int maxRows, CancellationToken cancellationToken = default)
        {
            if (maxRows <= 0)
            {
                return [];
            }

            _logger.Debug(() => $"Lade {maxRows} jüngste aktive Wiedergabestände.");

            // Compiled Query: Expression-Tree wurde einmalig beim Static-Init übersetzt;
            // hier nur noch Parameter-Bindung und Streaming-Materialisierung.
            List<RecentPlaybackRow> rows = [];
            await foreach (RecentPlaybackRow row in GetRecentActiveCompiled(_context, maxRows).ConfigureAwait(false))
            {
                rows.Add(row);
            }
            return rows;
        }

        // Server-seitige Aggregations-Projektion. Bewusst privat – kein API-Vertrag,
        // sondern reines Mapping-Ziel der GROUP-BY-Query.
        private sealed class PlaybackCountRow
        {
            public int Total { get; set; }
            public int Finished { get; set; }
            public int InProgress { get; set; }
        }

        /// <inheritdoc />
        /// <param name="episodeIds">Parameter episodeIds.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<HashSet<Guid>> GetCompletedEpisodeIdsAsync(IReadOnlyList<Guid> episodeIds, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(episodeIds);

            if (episodeIds.Count == 0)
            {
                return [];
            }

            // Ein einziger Query: alle abgeschlossenen PlaybackStates für die übergebenen Episoden
            List<Guid> completedIds = await _context.PlaybackStates
                .Where(p => episodeIds.Contains(p.EpisodeId) && p.IsCompleted)
                .Select(p => p.EpisodeId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            return completedIds.ToHashSet();
        }

        /// <summary>
        /// Markiert einen Wiedergabestatus als logisch gelöscht.
        /// Diese Methode bildet die unterste Ebene der Soft-Delete-Matrix ab:
        /// Ein PlaybackState besitzt keine abhängigen Kindelemente und kann isoliert logisch gelöscht werden, ohne andere Entitäten zu beeinflussen.
        /// </summary>
        /// <param name="id">Die eindeutige ID des zu löschenden Wiedergabestatus.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            PlaybackState? playbackState = await _context.PlaybackStates
                .LoadTrackedByIdOrWarnAsync(_logger, id, "PlaybackState", "Soft-Delete", cancellationToken).ConfigureAwait(false);

            if (playbackState is null)
            {
                // Wenn kein Wiedergabestatus existiert, ist kein Soft-Delete erforderlich.
                return;
            }

            // Der Wiedergabestatus wird ausschließlich über die definierte Domänenoperation logisch gelöscht.
            playbackState.MarkAsDeleted(EntityClock.Current.UtcNow);

            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("PlaybackState (ID: {PlaybackStateId}) als gelöscht markiert.", id);
        }

        /// <inheritdoc />
        public async Task MarkCompletedAsync(Guid episodeId, DateTime completedAt, CancellationToken cancellationToken = default)
        {
            PlaybackState? existing = await GetByEpisodeIdAsync(episodeId, cancellationToken).ConfigureAwait(false);

            if (existing is not null)
            {
                existing.IsCompleted = true;
                existing.CompletedAt = completedAt;
                await UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await AddAsync(new PlaybackState
                {
                    EpisodeId = episodeId,
                    IsCompleted = true,
                    CompletedAt = completedAt,
                    LastPlayedAt = completedAt
                }, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task MarkNotStartedAsync(Guid episodeId, CancellationToken cancellationToken = default)
        {
            PlaybackState? existing = await GetByEpisodeIdAsync(episodeId, cancellationToken).ConfigureAwait(false);

            if (existing is not null)
            {
                await DeleteAsync(existing.Id, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
