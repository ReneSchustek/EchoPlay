using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Internal;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace EchoPlay.Data.Services
{
    /// <summary>
    /// EF-Core-Umsetzung des Veröffentlichungskalenders.
    /// </summary>
    /// <param name="context">Der Datenbankkontext.</param>
    /// <param name="loggerFactory">Fabrik für den Protokollkanal.</param>
    public sealed class ReleaseCalendarDataService(
        EchoPlayDbContext context,
        EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory) : IReleaseCalendarDataService
    {
        private readonly EchoPlayDbContext _context = context;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger =
            loggerFactory.CreateLogger("ReleaseCalendarDataService");

        /// <inheritdoc />
        public async Task<IReadOnlyList<ReleaseCalendarEntry>> GetBetweenAsync(
            DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
        {
            return await _context.ReleaseCalendarEntries
                .Include(e => e.Series)
                .Where(e => e.ReleaseDate >= fromUtc && e.ReleaseDate < toUtc)
                .OrderBy(e => e.ReleaseDate)
                .ThenBy(e => e.Series.Title)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task<DateTime?> GetLatestReleaseDateAsync(CancellationToken cancellationToken = default)
        {
            return _context.ReleaseCalendarEntries
                .MaxAsync(e => (DateTime?)e.ReleaseDate, cancellationToken);
        }

        /// <inheritdoc />
        public Task<DateTime?> GetLastRefreshAsync(CancellationToken cancellationToken = default)
        {
            return _context.AppSettings
                .Select(s => s.ReleaseCalendarRefreshedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task SetLastRefreshAsync(DateTime refreshedAtUtc, CancellationToken cancellationToken = default)
        {
            // Ein gezieltes UPDATE statt Laden und Speichern der ganzen Zeile: Speichert die
            // Einstellungsseite in diesem Moment, bleiben ihre Werte erhalten.
            _ = await _context.AppSettings
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(s => s.ReleaseCalendarRefreshedAtUtc, refreshedAtUtc),
                    cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task UpsertRangeAsync(IReadOnlyList<ReleaseCalendarEntry> entries, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entries);

            if (entries.Count == 0)
            {
                return;
            }

            // Dasselbe Album kann über zwei Serien hereinkommen, wenn deren Namenssuche beim
            // Anbieter denselben Künstler trifft. Der Schlüssel ist eindeutig, also gewinnt der letzte.
            ProviderReleaseUpsertResult result = await ProviderReleaseUpsert
                .UpsertAsync(_context, _context.ReleaseCalendarEntries, entries, EntityClock.Current.UtcNow, cancellationToken)
                .ConfigureAwait(false);
            if (result.Conflict is not null)
            {
                // Ein zweiter Kontext hat dasselbe Album gleichzeitig eingetragen; sein Wert ist gleichwertig.
                _logger.Warning("UNIQUE-Konflikt beim Kalender-Upsert ignoriert: {Reason}", result.Conflict.InnerException?.Message);
                return;
            }

            _logger.Debug(() => $"Veröffentlichungskalender: {result.Inserted} neu, {result.Updated} aktualisiert.");
        }

        /// <inheritdoc />
        public async Task<int> RemoveOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
        {
            // Ein einziges DELETE: Räumt ein zweiter Kontext dieselben Zeilen gleichzeitig weg,
            // trifft dieser Aufruf eben weniger, statt an fehlenden Zeilen zu scheitern.
            int deleted = await _context.ReleaseCalendarEntries
                .Where(e => e.ReleaseDate < cutoffUtc)
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            if (deleted > 0)
            {
                _logger.Info(
                    "{ExpiredCount} vergangene Veröffentlichung(en) aus dem Kalender entfernt (Stichtag: {Cutoff}).",
                    deleted, cutoffUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }

            return deleted;
        }
    }
}
