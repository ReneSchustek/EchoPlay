using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Internal;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Services
{
    /// <summary>
    /// Implementierung des <see cref="IDatabaseMaintenanceService"/>.
    /// Nutzt EF8-Bulk-Delete (<c>ExecuteDeleteAsync</c>) für effiziente Massenbereinigungen –
    /// kein Laden in den Change-Tracker, direkte DELETE-Statements gegen SQLite.
    /// </summary>
    /// <param name="context">Der EF-Core-Kontext für direkte DB-Operationen.</param>
    public sealed class DatabaseMaintenanceService(EchoPlayDbContext context) : IDatabaseMaintenanceService
    {
        private readonly EchoPlayDbContext _context = context;

        /// <inheritdoc/>
        /// <param name="retentionDays">Parameter retentionDays.</param>
        public async Task PurgeAsync(int retentionDays)
        {
            DateTime cutoff = EntityClock.Current.UtcNow.AddDays(-retentionDays);

            // Phase 1: Kinder gelöschter Episoden bereinigen.
            // Reihenfolge wichtig – FK-Einschränkungen verbieten Löschen mit aktiven Referenzen.

            // LocalTracks und PlaybackStates für einzeln gelöschte Episoden entfernen
            _ = await _context.LocalTracks
                .IgnoreQueryFilters()
                .Where(t => t.Episode.IsDeleted && t.Episode.DeletedAt < cutoff)
                .ExecuteDeleteAsync().ConfigureAwait(false);

            _ = await _context.PlaybackStates
                .IgnoreQueryFilters()
                .Where(p => p.Episode.IsDeleted && p.Episode.DeletedAt < cutoff)
                .ExecuteDeleteAsync().ConfigureAwait(false);

            // Gelöschte Episoden selbst entfernen
            _ = await _context.Episodes
                .IgnoreQueryFilters()
                .Where(e => e.IsDeleted && e.DeletedAt < cutoff)
                .ExecuteDeleteAsync().ConfigureAwait(false);

            // Phase 2: Kinder gelöschter Serien bereinigen.
            // Nicht alle Episoden einer gelöschten Serie müssen selbst IsDeleted=true haben –
            // daher explizit über die Series-Navigation navigieren.
            _ = await _context.LocalTracks
                .IgnoreQueryFilters()
                .Where(t => t.Episode.Series.IsDeleted && t.Episode.Series.DeletedAt < cutoff)
                .ExecuteDeleteAsync().ConfigureAwait(false);

            _ = await _context.PlaybackStates
                .IgnoreQueryFilters()
                .Where(p => p.Episode.Series.IsDeleted && p.Episode.Series.DeletedAt < cutoff)
                .ExecuteDeleteAsync().ConfigureAwait(false);

            _ = await _context.Episodes
                .IgnoreQueryFilters()
                .Where(e => e.Series.IsDeleted && e.Series.DeletedAt < cutoff)
                .ExecuteDeleteAsync().ConfigureAwait(false);

            // Serien selbst als letztes – alle abhängigen Einträge wurden bereits entfernt
            _ = await _context.Series
                .IgnoreQueryFilters()
                .Where(s => s.IsDeleted && s.DeletedAt < cutoff)
                .ExecuteDeleteAsync().ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task OptimizeAsync()
        {
            // PRAGMA optimize analysiert die seit dem letzten Aufruf gesammelten Query-Statistiken
            // und aktualisiert bei Bedarf die internen Indizes des SQLite-Query-Planers.
            // Am effektivsten am Ende einer Sitzung, wenn die App die meisten Abfragen durchlaufen hat.
            _ = await _context.Database.ExecuteSqlRawAsync("PRAGMA optimize;").ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task VacuumAsync()
        {
            // VACUUM schreibt die gesamte SQLite-Datei neu – freigegebene Seiten werden zurückgewonnen.
            // Der Befehl ist synchron auf DB-Ebene, daher via ExecuteSqlRawAsync aufrufen.
            _ = await _context.Database.ExecuteSqlRawAsync("VACUUM;").ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task ClearLibraryAsync()
        {
            // Reihenfolge ist FK-kritisch: zuerst Blatt-Tabellen, dann Wurzel-Tabellen.
            // EF Core erzeugt direkte DELETE-Statements – kein Laden in den Change-Tracker.

            _ = await _context.LocalTracks
                .IgnoreQueryFilters()
                .ExecuteDeleteAsync().ConfigureAwait(false);

            _ = await _context.PlaybackStates
                .IgnoreQueryFilters()
                .ExecuteDeleteAsync().ConfigureAwait(false);

            _ = await _context.Episodes
                .IgnoreQueryFilters()
                .ExecuteDeleteAsync().ConfigureAwait(false);

            _ = await _context.Series
                .IgnoreQueryFilters()
                .ExecuteDeleteAsync().ConfigureAwait(false);

            // Alle Cover löschen – keine Entity-Zuordnung mehr vorhanden
            _ = await _context.CoverImages
                .IgnoreQueryFilters()
                .ExecuteDeleteAsync().ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task ClearOnlineLibraryAsync()
        {
            // Nur online-importierte Serien und deren abhängige Daten löschen.
            // FK-Reihenfolge: LocalTracks → PlaybackStates → Episodes → Series.
            List<Guid> onlineSeriesIds = await _context.Series
                .IgnoreQueryFilters()
                .Where(s => s.IsOnlineImported)
                .Select(s => s.Id)
                .ToListAsync().ConfigureAwait(false);

            if (onlineSeriesIds.Count == 0) return;

            await DeleteSeriesCascadeAsync(onlineSeriesIds).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task ClearLocalLibraryAsync()
        {
            // Alle LocalTracks entfernen
            _ = await _context.LocalTracks
                .IgnoreQueryFilters()
                .ExecuteDeleteAsync().ConfigureAwait(false);

            // Lokale Pfade von allen Episoden und Serien entfernen
            _ = await _context.Episodes
                .IgnoreQueryFilters()
                .Where(e => e.LocalFolderPath != null)
                .ExecuteUpdateAsync(e => e.SetProperty(
                    ep => ep.LocalFolderPath, (string?)null)).ConfigureAwait(false);

            _ = await _context.Series
                .IgnoreQueryFilters()
                .Where(s => s.LocalFolderPath != null)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    se => se.LocalFolderPath, (string?)null)).ConfigureAwait(false);

            // Rein lokale Serien (nicht online-importiert) komplett löschen
            List<Guid> localOnlySeriesIds = await _context.Series
                .IgnoreQueryFilters()
                .Where(s => !s.IsOnlineImported)
                .Select(s => s.Id)
                .ToListAsync().ConfigureAwait(false);

            if (localOnlySeriesIds.Count > 0)
            {
                // LocalTracks wurden oben bereits vollständig gelöscht; der (idempotente)
                // LocalTracks-Delete in der Kaskade ist hier ein No-Op.
                await DeleteSeriesCascadeAsync(localOnlySeriesIds).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Löscht die angegebenen Serien und alle abhängigen Daten in FK-korrekter Reihenfolge:
        /// LocalTracks → PlaybackStates → Episodes → Series → CoverImages (Episode + Series).
        /// </summary>
        /// <param name="seriesIds">Die IDs der zu löschenden Serien.</param>
        private async Task DeleteSeriesCascadeAsync(IReadOnlyList<Guid> seriesIds)
        {
            List<Guid> episodeIds = await _context.Episodes
                .IgnoreQueryFilters()
                .Where(e => seriesIds.Contains(e.SeriesId))
                .Select(e => e.Id)
                .ToListAsync().ConfigureAwait(false);

            if (episodeIds.Count > 0)
            {
                _ = await _context.LocalTracks
                    .IgnoreQueryFilters()
                    .Where(t => episodeIds.Contains(t.EpisodeId))
                    .ExecuteDeleteAsync().ConfigureAwait(false);

                _ = await _context.PlaybackStates
                    .IgnoreQueryFilters()
                    .Where(ps => episodeIds.Contains(ps.EpisodeId))
                    .ExecuteDeleteAsync().ConfigureAwait(false);
            }

            _ = await _context.Episodes
                .IgnoreQueryFilters()
                .Where(e => seriesIds.Contains(e.SeriesId))
                .ExecuteDeleteAsync().ConfigureAwait(false);

            _ = await _context.Series
                .IgnoreQueryFilters()
                .Where(s => seriesIds.Contains(s.Id))
                .ExecuteDeleteAsync().ConfigureAwait(false);

            // Cover der gelöschten Episoden und Serien entfernen
            if (episodeIds.Count > 0)
            {
                _ = await _context.CoverImages
                    .Where(c => c.EntityType == CoverEntityTypes.Episode && episodeIds.Contains(c.EntityId))
                    .ExecuteDeleteAsync().ConfigureAwait(false);
            }

            _ = await _context.CoverImages
                .Where(c => c.EntityType == CoverEntityTypes.Series && seriesIds.Contains(c.EntityId))
                .ExecuteDeleteAsync().ConfigureAwait(false);
        }
    }
}
