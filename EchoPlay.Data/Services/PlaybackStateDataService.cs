using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Infrastructure;
using EchoPlay.Data.Services.Interfaces;
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

        /// <summary>
        /// Liefert alle aktiven (nicht gelöschten) Wiedergabestände als flache Liste.
        /// Der globale Query-Filter des DbContext schließt logisch gelöschte Einträge automatisch aus.
        /// </summary>
        public async Task<IReadOnlyList<PlaybackState>> GetAllAsync()
        {
            _logger.Debug("Lade alle Wiedergabestände.");
            return await _context.PlaybackStates.ToListAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Liefert den Wiedergabestatus zu einer Episode oder <c>null</c>, wenn kein entsprechender Eintrag existiert oder dieser logisch
        /// gelöscht wurde.
        /// Da die EpisodeId ein fachlicher Suchschlüssel ist, wird hier FirstOrDefaultAsync verwendet.
        /// </summary>
        /// <param name="episodeId">Die eindeutige ID der Episode.</param>
        public async Task<PlaybackState?> GetByEpisodeIdAsync(Guid episodeId)
        {
            _logger.Debug($"Lade PlaybackState für Episode '{episodeId}'.");
            return await _context.PlaybackStates
                .FirstOrDefaultAsync(state => state.EpisodeId == episodeId).ConfigureAwait(false);
        }

        /// <summary>
        /// Fügt einen neuen Wiedergabestatus dauerhaft hinzu.
        /// </summary>
        /// <param name="playbackState">Der zu persistierende Wiedergabestatus.</param>
        public async Task AddAsync(PlaybackState playbackState)
        {
            _ = _context.PlaybackStates.Add(playbackState);

            try
            {
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (UniqueConstraintHandler.IsUniqueViolation(ex))
            {
                // Paralleler Scope hat bereits einen PlaybackState für dieselbe Episode angelegt.
                // Der erste Eintrag gewinnt — redundante Einfügung ignorieren.
                _logger.Warning($"UNIQUE-Konflikt beim Anlegen von PlaybackState für Episode '{playbackState.EpisodeId}' ignoriert.");
                return;
            }

            _logger.Info($"PlaybackState (ID: {playbackState.Id}) für Episode '{playbackState.EpisodeId}' hinzugefügt.");
        }

        /// <summary>
        /// Aktualisiert einen bestehenden Wiedergabestatus.
        /// Es wird davon ausgegangen, dass die Entität bereits aus dem aktuellen DbContext stammt.
        /// </summary>
        /// <param name="playbackState">Der zu aktualisierende Wiedergabestatus.</param>
        public async Task UpdateAsync(PlaybackState playbackState)
        {
            _ = _context.PlaybackStates.Update(playbackState);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            _logger.Info($"PlaybackState (ID: {playbackState.Id}) für Episode '{playbackState.EpisodeId}' aktualisiert.");
        }

        /// <summary>
        /// Berechnet aggregierte Wiedergabe-Zähler für alle Episoden einer Serie.
        /// Nutzt einen Join zwischen Episodes und PlaybackStates, um in einer einzigen Datenbankabfrage
        /// alle Zähler zu ermitteln – ersetzt das N+1-Muster aus dem alten LoadAsync.
        /// </summary>
        /// <param name="seriesId">ID der Serie, deren Episoden ausgewertet werden.</param>
        /// <returns>
        /// Tuple (Finished, InProgress, NotStarted). Gibt (0, 0, 0) zurück,
        /// wenn für die Serie keine Episoden existieren.
        /// </returns>
        public async Task<(int Finished, int InProgress, int NotStarted)> GetCountsBySeriesIdAsync(Guid seriesId)
        {
            _logger.Debug($"Lade aggregierte Wiedergabezähler für Serie '{seriesId}'.");

            // Episode-IDs der Serie laden (nur IDs, keine Blobs)
            List<Guid> episodeIds = await _context.Episodes

                .Where(e => e.SeriesId == seriesId)
                .Select(e => e.Id)
                .ToListAsync().ConfigureAwait(false);

            if (episodeIds.Count == 0) return (0, 0, 0);

            // PlaybackStates einmal laden und im Speicher zählen.
            // TimeSpan-Vergleich kann SQLite nicht in einem JOIN übersetzen,
            // daher Client-Evaluation – bei <500 States pro Serie kein Problem.
            List<PlaybackState> states = await _context.PlaybackStates

                .Where(p => episodeIds.Contains(p.EpisodeId))
                .ToListAsync().ConfigureAwait(false);

            int finished = states.Count(s => s.IsCompleted);
            int inProgress = states.Count(s => !s.IsCompleted && s.LastPosition > TimeSpan.Zero);
            int notStarted = episodeIds.Count - finished - inProgress;

            return (finished, inProgress, notStarted);
        }

        /// <inheritdoc />
        public async Task<HashSet<Guid>> GetCompletedEpisodeIdsAsync(IReadOnlyList<Guid> episodeIds)
        {
            if (episodeIds.Count == 0)
            {
                return [];
            }

            // Ein einziger Query: alle abgeschlossenen PlaybackStates für die übergebenen Episoden
            List<Guid> completedIds = await _context.PlaybackStates
                .Where(p => episodeIds.Contains(p.EpisodeId) && p.IsCompleted)
                .Select(p => p.EpisodeId)
                .ToListAsync().ConfigureAwait(false);

            return completedIds.ToHashSet();
        }

        /// <summary>
        /// Markiert einen Wiedergabestatus als logisch gelöscht.
        /// Diese Methode bildet die unterste Ebene der Soft-Delete-Matrix ab:
        /// Ein PlaybackState besitzt keine abhängigen Kindelemente und kann isoliert logisch gelöscht werden, ohne andere Entitäten zu beeinflussen.
        /// </summary>
        /// <param name="id">Die eindeutige ID des zu löschenden Wiedergabestatus.</param>
        public async Task DeleteAsync(Guid id)
        {
            PlaybackState? playbackState = await _context.PlaybackStates
                .AsTracking()
                .FirstOrDefaultAsync(p => p.Id == id).ConfigureAwait(false);

            if (playbackState == null)
            {
                // Wenn kein Wiedergabestatus existiert, ist kein Soft-Delete erforderlich.
                _logger.Warning($"PlaybackState mit ID '{id}' nicht gefunden – Soft-Delete übersprungen.");
                return;
            }

            // Der Wiedergabestatus wird ausschließlich über die definierte Domänenoperation logisch gelöscht.
            playbackState.MarkAsDeleted(DateTime.UtcNow);

            await _context.SaveChangesAsync().ConfigureAwait(false);
            _logger.Info($"PlaybackState (ID: {id}) als gelöscht markiert.");
        }
    }
}
