using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Internal;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

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
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<IReadOnlyList<CachedNewRelease>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            List<CachedNewRelease> result = await _context.CachedNewReleases

                .Include(c => c.Series)
                .OrderByDescending(c => c.ReleaseDate)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            _logger.Debug(() => $"{result.Count} gecachte Neuerscheinung(en) geladen.");
            return result;
        }

        /// <inheritdoc />
        /// <param name="seriesId">Parameter seriesId.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<IReadOnlyList<CachedNewRelease>> GetBySeriesIdAsync(Guid seriesId, CancellationToken cancellationToken = default)
        {
            List<CachedNewRelease> result = await _context.CachedNewReleases

                .Where(c => c.SeriesId == seriesId)
                .OrderByDescending(c => c.ReleaseDate)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            return result;
        }

        /// <inheritdoc />
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<DateTime?> GetLatestCheckTimeAsync(CancellationToken cancellationToken = default)
        {
            // Max auf leerer Menge gibt null zurück (nullable DateTime)
            DateTime? latest = await _context.CachedNewReleases

                .MaxAsync(c => (DateTime?)c.CheckedAtUtc, cancellationToken).ConfigureAwait(false);

            return latest;
        }

        /// <inheritdoc />
        /// <param name="entries">Parameter entries.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task UpsertRangeAsync(IReadOnlyList<CachedNewRelease> entries, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entries);

            if (entries.Count == 0)
            {
                return;
            }

            // Eingabe deduplizieren: dieselbe CollectionId kann in mehreren Serien vorkommen
            Dictionary<long, CachedNewRelease> uniqueByCollectionId = new();
            foreach (CachedNewRelease entry in entries)
            {
                uniqueByCollectionId[entry.CollectionId] = entry;
            }

            // Alle betroffenen CollectionIds in einem Batch laden (vermeidet N+1-Abfragen).
            // IgnoreQueryFilters: auch soft-gelöschte Einträge finden, da der UNIQUE-Index
            // auf CollectionId unabhängig von IsDeleted gilt.
            List<long> collectionIds = uniqueByCollectionId.Keys.ToList();
            Dictionary<long, CachedNewRelease> existingByCollectionId = await _context.CachedNewReleases
                .AsTracking()
                .IgnoreQueryFilters()
                .Where(c => collectionIds.Contains(c.CollectionId))
                .ToDictionaryAsync(c => c.CollectionId, cancellationToken).ConfigureAwait(false);

            int insertCount = 0;
            int updateCount = 0;

            foreach (CachedNewRelease entry in uniqueByCollectionId.Values)
            {
                if (existingByCollectionId.TryGetValue(entry.CollectionId, out CachedNewRelease? existing))
                {
                    // Bestehenden Eintrag aktualisieren (Titel, Cover, Datum können sich ändern).
                    // Soft-gelöschte Einträge physisch entfernen und neu anlegen,
                    // da Cache-Einträge keine fachlichen Daten sind.
                    if (existing.IsDeleted)
                    {
                        _ = _context.CachedNewReleases.Remove(existing);
                        _ = _context.CachedNewReleases.Add(entry);
                        insertCount++;
                    }
                    else
                    {
                        existing.Title = entry.Title;
                        existing.SeriesId = entry.SeriesId;
                        existing.EpisodeNumber = entry.EpisodeNumber;
                        existing.ReleaseDate = entry.ReleaseDate;
                        existing.CoverUrl = entry.CoverUrl;
                        existing.CheckedAtUtc = entry.CheckedAtUtc;
                        existing.MarkAsUpdated(EntityClock.Current.UtcNow);
                        updateCount++;
                    }
                }
                else
                {
                    _ = _context.CachedNewReleases.Add(entry);
                    insertCount++;
                }
            }

            DbUpdateException? conflict = await _context.TrySaveChangesIgnoreUniqueAsync(cancellationToken).ConfigureAwait(false);
            if (conflict is not null)
            {
                // Paralleler Scope hat denselben CollectionId eingefügt — ignorieren,
                // da Cache-Einträge redundant sind und der andere Wert gleichwertig ist.
                _logger.Warning("UNIQUE-Konflikt beim Cache-Upsert ignoriert: {Reason}", conflict.InnerException?.Message);
                return;
            }

            _logger.Info("Neuerscheinungen-Cache aktualisiert: {InsertCount} neu, {UpdateCount} aktualisiert.", insertCount, updateCount);
        }

        /// <inheritdoc />
        /// <param name="cutoff">Parameter cutoff.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<int> RemoveOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default)
        {
            // Einträge mit ReleaseDate vor dem Cutoff UND in der Vergangenheit entfernen.
            // Ankündigungen (Zukunft) bleiben erhalten, auch wenn sie rechnerisch
            // vor dem Cutoff liegen könnten (z.B. bei sehr kurzem Zeitfenster).
            // Zeit wird vor der Query erfasst, da EF Core den Aufruf
            // nicht zuverlässig in SQL übersetzen kann.
            DateTime now = EntityClock.Current.UtcNow;
            List<CachedNewRelease> expired = await _context.CachedNewReleases
                .AsTracking()
                .Where(c => c.ReleaseDate < cutoff && c.ReleaseDate < now)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            if (expired.Count == 0)
            {
                return 0;
            }

            // Physisches Löschen: Cache-Einträge sind keine fachlichen Daten,
            // sondern API-Ergebnisse – Soft-Delete wäre hier unnötig.
            _context.CachedNewReleases.RemoveRange(expired);
            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.Info(
                "{ExpiredCount} abgelaufene Neuerscheinung(en) aus dem Cache entfernt (Cutoff: {Cutoff}).",
                expired.Count, cutoff.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            return expired.Count;
        }

        /// <inheritdoc />
        /// <param name="seriesIds">Parameter seriesIds.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<int> RemoveBySeriesIdsAsync(IReadOnlyList<Guid> seriesIds, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(seriesIds);

            if (seriesIds.Count == 0)
            {
                return 0;
            }

            // Physisches Löschen: Cache-Einträge sind API-Ergebnisse, kein Soft-Delete nötig.
            int deleted = await _context.CachedNewReleases
                .Where(c => seriesIds.Contains(c.SeriesId))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            if (deleted > 0)
            {
                _logger.Info("{DeletedCount} Cache-Eintrag/Einträge für {SeriesCount} nicht-überwachte Serie(n) entfernt.", deleted, seriesIds.Count);
            }

            return deleted;
        }

        /// <inheritdoc />
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task ClearAllAsync(CancellationToken cancellationToken = default)
        {
            // ExecuteDeleteAsync: ein SQL-DELETE statt Laden + Entfernen aller Entities.
            // Effizienter, da die Einträge nicht erst in den Speicher geladen werden.
            int deleted = await _context.CachedNewReleases.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            if (deleted > 0)
            {
                _logger.Info("Neuerscheinungen-Cache vollständig geleert ({DeletedCount} Einträge).", deleted);
            }
        }
    }
}
