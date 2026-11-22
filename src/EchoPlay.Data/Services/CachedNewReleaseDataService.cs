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
        /// <param name="seriesId">Datenbank-ID der Serie.</param>
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
        /// <param name="entries">Die zu schreibenden Einträge; vorhandene werden anhand ihrer Kennung aktualisiert.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task UpsertRangeAsync(IReadOnlyList<CachedNewRelease> entries, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entries);

            if (entries.Count == 0)
            {
                return;
            }

            // Eingabe deduplizieren: dieselbe CollectionId kann in mehreren Serien vorkommen.
            ProviderReleaseUpsertResult result = await ProviderReleaseUpsert
                .UpsertAsync(_context, _context.CachedNewReleases, entries, EntityClock.Current.UtcNow, cancellationToken)
                .ConfigureAwait(false);
            if (result.Conflict is not null)
            {
                // Paralleler Scope hat denselben CollectionId eingefügt. Ignorieren,
                // da Cache-Einträge redundant sind und der andere Wert gleichwertig ist.
                _logger.Warning("UNIQUE-Konflikt beim Cache-Upsert ignoriert: {Reason}", result.Conflict.InnerException?.Message);
                return;
            }

            _logger.Info("Neuerscheinungen-Cache aktualisiert: {InsertCount} neu, {UpdateCount} aktualisiert.", result.Inserted, result.Updated);
        }

        /// <inheritdoc />
        /// <param name="cutoff">Stichtag; alles davor wird entfernt.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<int> RemoveOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default)
        {
            // Einträge mit ReleaseDate vor dem Cutoff UND in der Vergangenheit entfernen.
            // Ankündigungen (Zukunft) bleiben erhalten, auch wenn sie rechnerisch
            // vor dem Cutoff liegen könnten (z.B. bei sehr kurzem Zeitfenster).
            // Zeit wird vor der Query erfasst, da EF Core den Aufruf
            // nicht zuverlässig in SQL übersetzen kann.
            DateTime now = EntityClock.Current.UtcNow;

            // Physisches Löschen: Cache-Einträge sind keine fachlichen Daten,
            // sondern API-Ergebnisse, Soft-Delete wäre hier unnötig.
            // Ein einziges DELETE statt Laden und Entfernen: Räumt ein zweiter Kontext auf
            // derselben Datei dieselben Zeilen gleichzeitig weg, trifft dieser Aufruf eben
            // weniger Zeilen. Der Weg über den Änderungsverfolger erwartete dagegen jede
            // geladene Zeile und brach mit DbUpdateConcurrencyException ab, wenn sie fehlte.
            int deleted = await _context.CachedNewReleases
                .Where(c => c.ReleaseDate < cutoff && c.ReleaseDate < now)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            if (deleted > 0)
            {
                _logger.Info(
                    "{ExpiredCount} abgelaufene Neuerscheinung(en) aus dem Cache entfernt (Cutoff: {Cutoff}).",
                    deleted, cutoff.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }

            return deleted;
        }

        /// <inheritdoc />
        /// <param name="seriesIds">Datenbank-IDs der betroffenen Serien.</param>
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
