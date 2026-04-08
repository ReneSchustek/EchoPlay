using EchoPlay.Data.Context;
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
        public async Task PurgeAsync(int retentionDays)
        {
            DateTime cutoff = DateTime.UtcNow.AddDays(-retentionDays);

            // Phase 1: Kinder gelöschter Episoden bereinigen.
            // Reihenfolge wichtig – FK-Einschränkungen verbieten Löschen mit aktiven Referenzen.

            // LocalTracks und PlaybackStates für einzeln gelöschte Episoden entfernen
            await _context.LocalTracks
                .IgnoreQueryFilters()
                .Where(t => t.Episode.IsDeleted && t.Episode.DeletedAt < cutoff)
                .ExecuteDeleteAsync().ConfigureAwait(false);

            await _context.PlaybackStates
                .IgnoreQueryFilters()
                .Where(p => p.Episode.IsDeleted && p.Episode.DeletedAt < cutoff)
                .ExecuteDeleteAsync().ConfigureAwait(false);

            // Gelöschte Episoden selbst entfernen
            await _context.Episodes
                .IgnoreQueryFilters()
                .Where(e => e.IsDeleted && e.DeletedAt < cutoff)
                .ExecuteDeleteAsync().ConfigureAwait(false);

            // Phase 2: Kinder gelöschter Serien bereinigen.
            // Nicht alle Episoden einer gelöschten Serie müssen selbst IsDeleted=true haben –
            // daher explizit über die Series-Navigation navigieren.
            await _context.LocalTracks
                .IgnoreQueryFilters()
                .Where(t => t.Episode.Series.IsDeleted && t.Episode.Series.DeletedAt < cutoff)
                .ExecuteDeleteAsync().ConfigureAwait(false);

            await _context.PlaybackStates
                .IgnoreQueryFilters()
                .Where(p => p.Episode.Series.IsDeleted && p.Episode.Series.DeletedAt < cutoff)
                .ExecuteDeleteAsync().ConfigureAwait(false);

            await _context.Episodes
                .IgnoreQueryFilters()
                .Where(e => e.Series.IsDeleted && e.Series.DeletedAt < cutoff)
                .ExecuteDeleteAsync().ConfigureAwait(false);

            // Serien selbst als letztes – alle abhängigen Einträge wurden bereits entfernt
            await _context.Series
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
            await _context.Database.ExecuteSqlRawAsync("PRAGMA optimize;").ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task VacuumAsync()
        {
            // VACUUM schreibt die gesamte SQLite-Datei neu – freigegebene Seiten werden zurückgewonnen.
            // Der Befehl ist synchron auf DB-Ebene, daher via ExecuteSqlRawAsync aufrufen.
            await _context.Database.ExecuteSqlRawAsync("VACUUM;").ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task ClearLibraryAsync()
        {
            // Reihenfolge ist FK-kritisch: zuerst Blatt-Tabellen, dann Wurzel-Tabellen.
            // EF Core erzeugt direkte DELETE-Statements – kein Laden in den Change-Tracker.

            await _context.LocalTracks
                .IgnoreQueryFilters()
                .ExecuteDeleteAsync().ConfigureAwait(false);

            await _context.PlaybackStates
                .IgnoreQueryFilters()
                .ExecuteDeleteAsync().ConfigureAwait(false);

            await _context.Episodes
                .IgnoreQueryFilters()
                .ExecuteDeleteAsync().ConfigureAwait(false);

            await _context.Series
                .IgnoreQueryFilters()
                .ExecuteDeleteAsync().ConfigureAwait(false);

            // Alle Cover löschen – keine Entity-Zuordnung mehr vorhanden
            await _context.CoverImages
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

            List<Guid> episodeIds = await _context.Episodes
                .IgnoreQueryFilters()
                .Where(e => onlineSeriesIds.Contains(e.SeriesId))
                .Select(e => e.Id)
                .ToListAsync().ConfigureAwait(false);

            if (episodeIds.Count > 0)
            {
                await _context.LocalTracks
                    .IgnoreQueryFilters()
                    .Where(t => episodeIds.Contains(t.EpisodeId))
                    .ExecuteDeleteAsync().ConfigureAwait(false);

                await _context.PlaybackStates
                    .IgnoreQueryFilters()
                    .Where(ps => episodeIds.Contains(ps.EpisodeId))
                    .ExecuteDeleteAsync().ConfigureAwait(false);
            }

            await _context.Episodes
                .IgnoreQueryFilters()
                .Where(e => onlineSeriesIds.Contains(e.SeriesId))
                .ExecuteDeleteAsync().ConfigureAwait(false);

            await _context.Series
                .IgnoreQueryFilters()
                .Where(s => onlineSeriesIds.Contains(s.Id))
                .ExecuteDeleteAsync().ConfigureAwait(false);

            // Cover der gelöschten Online-Episoden und -Serien entfernen
            if (episodeIds.Count > 0)
            {
                await _context.CoverImages
                    .Where(c => c.EntityType == "Episode" && episodeIds.Contains(c.EntityId))
                    .ExecuteDeleteAsync().ConfigureAwait(false);
            }

            await _context.CoverImages
                .Where(c => c.EntityType == "Series" && onlineSeriesIds.Contains(c.EntityId))
                .ExecuteDeleteAsync().ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task ClearLocalLibraryAsync()
        {
            // Alle LocalTracks entfernen
            await _context.LocalTracks
                .IgnoreQueryFilters()
                .ExecuteDeleteAsync().ConfigureAwait(false);

            // Lokale Pfade von allen Episoden und Serien entfernen
            await _context.Episodes
                .IgnoreQueryFilters()
                .Where(e => e.LocalFolderPath != null)
                .ExecuteUpdateAsync(e => e.SetProperty(
                    ep => ep.LocalFolderPath, (string?)null)).ConfigureAwait(false);

            await _context.Series
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
                List<Guid> episodeIds = await _context.Episodes
                    .IgnoreQueryFilters()
                    .Where(e => localOnlySeriesIds.Contains(e.SeriesId))
                    .Select(e => e.Id)
                    .ToListAsync().ConfigureAwait(false);

                if (episodeIds.Count > 0)
                {
                    await _context.PlaybackStates
                        .IgnoreQueryFilters()
                        .Where(ps => episodeIds.Contains(ps.EpisodeId))
                        .ExecuteDeleteAsync().ConfigureAwait(false);
                }

                await _context.Episodes
                    .IgnoreQueryFilters()
                    .Where(e => localOnlySeriesIds.Contains(e.SeriesId))
                    .ExecuteDeleteAsync().ConfigureAwait(false);

                await _context.Series
                    .IgnoreQueryFilters()
                    .Where(s => localOnlySeriesIds.Contains(s.Id))
                    .ExecuteDeleteAsync().ConfigureAwait(false);

                // Cover der gelöschten lokalen Episoden und Serien entfernen
                if (episodeIds.Count > 0)
                {
                    await _context.CoverImages
                        .Where(c => c.EntityType == "Episode" && episodeIds.Contains(c.EntityId))
                        .ExecuteDeleteAsync().ConfigureAwait(false);
                }

                await _context.CoverImages
                    .Where(c => c.EntityType == "Series" && localOnlySeriesIds.Contains(c.EntityId))
                    .ExecuteDeleteAsync().ConfigureAwait(false);
            }
        }
    }
}
