using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Services
{
    /// <summary>
    /// EF-Core-basierte Implementierung des Datenservices für lokale Tracks einer Episode.
    /// </summary>
    /// <remarks>Initialisiert eine neue Instanz des <see cref="LocalTrackDataService"/>.</remarks>
    /// <param name="context">Der zu verwendende EF-Core-Datenbankkontext.</param>
    /// <param name="loggerFactory">Die Logger-Factory zur Erstellung des Loggers.</param>
    public sealed class LocalTrackDataService(
        EchoPlayDbContext context,
        EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory) : ILocalTrackDataService
    {
        private readonly EchoPlayDbContext _context = context;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger = loggerFactory.CreateLogger("LocalTrackDataService");

        /// <summary>
        /// Lädt alle lokalen Tracks einer Episode, sortiert nach Tracknummer.
        /// Gibt eine leere Liste zurück, wenn noch kein Scan für die Episode durchgeführt wurde.
        /// </summary>
        /// <param name="episodeId">ID der Episode.</param>
        /// <returns>Alle bekannten lokalen Tracks der Episode.</returns>
        public async Task<IReadOnlyList<LocalTrack>> GetByEpisodeIdAsync(Guid episodeId)
        {
            _logger.Debug($"Lade lokale Tracks für Episode '{episodeId}'.");

            List<LocalTrack> result = await _context.LocalTracks
                .AsNoTracking()
                .Where(track => track.EpisodeId == episodeId)
                .OrderBy(track => track.TrackNumber)
                .ToListAsync().ConfigureAwait(false);

            _logger.Debug($"{result.Count} Track(s) für Episode '{episodeId}' geladen.");

            return result;
        }

        /// <summary>
        /// Ersetzt alle lokalen Tracks einer Episode durch die übergebene Liste.
        /// Bestehende Einträge werden hart gelöscht, da sie aus einem deterministischen Scan stammen
        /// und kein eigenständiges fachliches Objekt darstellen.
        /// </summary>
        /// <param name="episodeId">ID der Episode.</param>
        /// <param name="tracks">Die neuen Tracks. Die <see cref="LocalTrack.EpisodeId"/> muss bereits gesetzt sein.</param>
        public async Task SaveTracksForEpisodeAsync(Guid episodeId, IReadOnlyList<LocalTrack> tracks)
        {
            _logger.Debug($"Speichere {tracks.Count} Track(s) für Episode '{episodeId}'.");

            // Vorhandene Einträge hart löschen – Scan-Ergebnisse sind keine Stammdaten
            List<LocalTrack> existing = await _context.LocalTracks
                .Where(track => track.EpisodeId == episodeId)
                .ToListAsync().ConfigureAwait(false);

            _context.LocalTracks.RemoveRange(existing);
            _context.LocalTracks.AddRange(tracks);

            await _context.SaveChangesAsync().ConfigureAwait(false);

            _logger.Info($"{tracks.Count} Track(s) für Episode '{episodeId}' gespeichert ({existing.Count} vorherige entfernt).");
        }
    }
}
