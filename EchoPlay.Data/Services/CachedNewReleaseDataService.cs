using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Services
{
    /// <summary>
    /// EF-Core-basierte Implementierung für den Neuerscheinungen-Cache.
    /// Verwaltet die in SQLite persistierten iTunes-Ergebnisse, damit das Dashboard
    /// beim Start sofort Neuerscheinungen anzeigen kann.
    /// </summary>
    /// <param name="context">Der zu verwendende EF-Core-Datenbankkontext.</param>
    /// <param name="loggerFactory">Die Logger-Factory zur Erstellung des Loggers.</param>
    public sealed class CachedNewReleaseDataService(
        EchoPlayDbContext context,
        EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory) : ICachedNewReleaseDataService
    {
        private readonly EchoPlayDbContext _context = context;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger =
            loggerFactory.CreateLogger("CachedNewReleaseDataService");

        /// <inheritdoc />
        public async Task<IReadOnlyList<CachedNewRelease>> GetAllAsync()
        {
            List<CachedNewRelease> result = await _context.CachedNewReleases
                .AsNoTracking()
                .Include(c => c.Series)
                .OrderByDescending(c => c.ReleaseDate)
                .ToListAsync().ConfigureAwait(false);

            _logger.Debug($"{result.Count} gecachte Neuerscheinung(en) geladen.");
            return result;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<CachedNewRelease>> GetBySeriesIdAsync(Guid seriesId)
        {
            List<CachedNewRelease> result = await _context.CachedNewReleases
                .AsNoTracking()
                .Where(c => c.SeriesId == seriesId)
                .OrderByDescending(c => c.ReleaseDate)
                .ToListAsync().ConfigureAwait(false);

            return result;
        }

        /// <inheritdoc />
        public async Task<DateTime?> GetLatestCheckTimeAsync()
        {
            // Max auf leerer Menge gibt null zurück (nullable DateTime)
            DateTime? latest = await _context.CachedNewReleases
                .AsNoTracking()
                .MaxAsync(c => (DateTime?)c.CheckedAtUtc).ConfigureAwait(false);

            return latest;
        }

        /// <inheritdoc />
        public async Task UpsertRangeAsync(IReadOnlyList<CachedNewRelease> entries)
        {
            if (entries.Count == 0)
            {
                return;
            }

            // Alle betroffenen CollectionIds in einem Batch laden (vermeidet N+1-Abfragen)
            List<long> collectionIds = entries.Select(e => e.CollectionId).ToList();
            Dictionary<long, CachedNewRelease> existingByCollectionId = await _context.CachedNewReleases
                .Where(c => collectionIds.Contains(c.CollectionId))
                .ToDictionaryAsync(c => c.CollectionId).ConfigureAwait(false);

            int insertCount = 0;
            int updateCount = 0;

            foreach (CachedNewRelease entry in entries)
            {
                if (existingByCollectionId.TryGetValue(entry.CollectionId, out CachedNewRelease? existing))
                {
                    // Bestehenden Eintrag aktualisieren (Titel, Cover, Datum können sich ändern)
                    existing.Title = entry.Title;
                    existing.EpisodeNumber = entry.EpisodeNumber;
                    existing.ReleaseDate = entry.ReleaseDate;
                    existing.CoverUrl = entry.CoverUrl;
                    existing.CheckedAtUtc = entry.CheckedAtUtc;
                    existing.MarkAsUpdated(DateTime.UtcNow);
                    updateCount++;
                }
                else
                {
                    _context.CachedNewReleases.Add(entry);
                    insertCount++;
                }
            }

            await _context.SaveChangesAsync().ConfigureAwait(false);

            _logger.Info($"Neuerscheinungen-Cache aktualisiert: {insertCount} neu, {updateCount} aktualisiert.");
        }

        /// <inheritdoc />
        public async Task<int> RemoveOlderThanAsync(DateTime cutoff)
        {
            // Einträge mit ReleaseDate vor dem Cutoff UND in der Vergangenheit entfernen.
            // Ankündigungen (Zukunft) bleiben erhalten, auch wenn sie rechnerisch
            // vor dem Cutoff liegen könnten (z.B. bei sehr kurzem Zeitfenster).
            // DateTime.UtcNow wird vor der Query erfasst, da EF Core den Aufruf
            // nicht zuverlässig in SQL übersetzen kann.
            DateTime now = DateTime.UtcNow;
            List<CachedNewRelease> expired = await _context.CachedNewReleases
                .Where(c => c.ReleaseDate < cutoff && c.ReleaseDate < now)
                .ToListAsync().ConfigureAwait(false);

            if (expired.Count == 0)
            {
                return 0;
            }

            // Physisches Löschen: Cache-Einträge sind keine fachlichen Daten,
            // sondern API-Ergebnisse – Soft-Delete wäre hier unnötig.
            _context.CachedNewReleases.RemoveRange(expired);
            await _context.SaveChangesAsync().ConfigureAwait(false);

            _logger.Info($"{expired.Count} abgelaufene Neuerscheinung(en) aus dem Cache entfernt (Cutoff: {cutoff:yyyy-MM-dd}).");
            return expired.Count;
        }

        /// <inheritdoc />
        public async Task<int> RemoveBySeriesIdsAsync(IReadOnlyList<Guid> seriesIds)
        {
            if (seriesIds.Count == 0)
            {
                return 0;
            }

            // Physisches Löschen: Cache-Einträge sind API-Ergebnisse, kein Soft-Delete nötig.
            int deleted = await _context.CachedNewReleases
                .Where(c => seriesIds.Contains(c.SeriesId))
                .ExecuteDeleteAsync().ConfigureAwait(false);

            if (deleted > 0)
            {
                _logger.Info($"{deleted} Cache-Eintrag/Einträge für {seriesIds.Count} nicht-überwachte Serie(n) entfernt.");
            }

            return deleted;
        }

        /// <inheritdoc />
        public async Task ClearAllAsync()
        {
            // ExecuteDeleteAsync: ein SQL-DELETE statt Laden + Entfernen aller Entities.
            // Effizienter, da die Einträge nicht erst in den Speicher geladen werden.
            int deleted = await _context.CachedNewReleases.ExecuteDeleteAsync().ConfigureAwait(false);

            if (deleted > 0)
            {
                _logger.Info($"Neuerscheinungen-Cache vollständig geleert ({deleted} Einträge).");
            }
        }
    }
}
