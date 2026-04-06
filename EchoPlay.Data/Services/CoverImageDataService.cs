using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.Data.Services
{
    /// <summary>
    /// EF-Core-basierte Implementierung für Cover-Binärdaten.
    /// Alle Blob-Zugriffe laufen über diese Klasse – Metadaten-Services laden nie Bilddaten.
    /// </summary>
    public sealed class CoverImageDataService(
        EchoPlayDbContext context,
        ILoggerFactory loggerFactory) : ICoverImageDataService
    {
        private readonly EchoPlayDbContext _context = context;
        private readonly ILogger _logger = loggerFactory.CreateLogger("CoverImageDataService");

        /// <inheritdoc/>
        public async Task<CoverImage?> GetByEntityAsync(string entityType, Guid entityId)
        {
            return await _context.CoverImages
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.EntityType == entityType && c.EntityId == entityId)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyDictionary<Guid, byte[]>> GetImageDataByEntitiesAsync(
            string entityType, IReadOnlyList<Guid> entityIds)
        {
            if (entityIds.Count == 0) return new Dictionary<Guid, byte[]>(0);

            // Ein einziger Query mit WHERE EntityId IN (...) – kein N+1
            List<CoverImage> covers = await _context.CoverImages
                .AsNoTracking()
                .Where(c => c.EntityType == entityType && entityIds.Contains(c.EntityId))
                .ToListAsync()
                .ConfigureAwait(false);

            return covers.ToDictionary(c => c.EntityId, c => c.ImageData);
        }

        /// <inheritdoc/>
        public async Task SetCoverAsync(string entityType, Guid entityId, byte[] imageData, string? sourceUrl = null)
        {
            // Upsert per SQL: INSERT OR REPLACE vermeidet Race Conditions zwischen
            // parallelen Scopes (z.B. RunOnceAsync im Splash + Start im Hintergrund).
            DateTime now = DateTime.UtcNow;

            CoverImage? existing = await _context.CoverImages
                .FirstOrDefaultAsync(c => c.EntityType == entityType && c.EntityId == entityId)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                existing.ImageData = imageData;
                existing.SourceUrl = sourceUrl ?? existing.SourceUrl;
                existing.LastChecked = now;
            }
            else
            {
                // Neuen Eintrag anlegen – bei UNIQUE-Konflikt (paralleler Insert)
                // wird der Fehler abgefangen und als Update wiederholt.
                try
                {
                    CoverImage cover = new()
                    {
                        EntityType = entityType,
                        EntityId = entityId,
                        ImageData = imageData,
                        SourceUrl = sourceUrl,
                        LastChecked = now
                    };
                    _context.CoverImages.Add(cover);
                    await _context.SaveChangesAsync().ConfigureAwait(false);
                    _logger.Debug($"Cover gespeichert: {entityType} {entityId}");
                    return;
                }
                catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
                {
                    // UNIQUE-Konflikt: ein anderer Scope hat parallel eingefügt.
                    // Change-Tracker zurücksetzen und als Update wiederholen.
                    _context.ChangeTracker.Clear();

                    CoverImage? retry = await _context.CoverImages
                        .FirstOrDefaultAsync(c => c.EntityType == entityType && c.EntityId == entityId)
                        .ConfigureAwait(false);

                    if (retry is not null)
                    {
                        retry.ImageData = imageData;
                        retry.SourceUrl = sourceUrl ?? retry.SourceUrl;
                        retry.LastChecked = now;
                    }
                }
            }

            await _context.SaveChangesAsync().ConfigureAwait(false);
            _logger.Debug($"Cover gespeichert: {entityType} {entityId}");
        }

        /// <inheritdoc/>
        public async Task SetLastCheckedAsync(string entityType, Guid entityId, DateTime checkedAt)
        {
            CoverImage? existing = await _context.CoverImages
                .FirstOrDefaultAsync(c => c.EntityType == entityType && c.EntityId == entityId)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                existing.LastChecked = checkedAt;
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
            else
            {
                // Kein Cover vorhanden → Platzhalter-Eintrag mit LastChecked aber ohne Bild,
                // damit der Background-Worker weiß, dass er schon gesucht hat
                CoverImage placeholder = new()
                {
                    EntityType = entityType,
                    EntityId = entityId,
                    ImageData = [],
                    LastChecked = checkedAt
                };
                _context.CoverImages.Add(placeholder);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<Guid>> GetUncheckedEntityIdsAsync(
            string entityType, DateTime cooldownThreshold, int limit)
        {
            // Entities die entweder noch nie geprüft wurden (kein Eintrag in CoverImages)
            // oder deren LastChecked abgelaufen ist UND kein Cover haben
            // Für Serien: alle Series-IDs ohne Cover-Eintrag oder mit abgelaufenem Check
            // Für Episoden: alle Episode-IDs ohne Cover-Eintrag oder mit abgelaufenem Check

            // Entities MIT abgelaufenem Check und OHNE Bild
            List<Guid> expired = await _context.CoverImages
                .AsNoTracking()
                .Where(c => c.EntityType == entityType
                    && c.ImageData.Length == 0
                    && (c.LastChecked == null || c.LastChecked < cooldownThreshold))
                .Select(c => c.EntityId)
                .Take(limit)
                .ToListAsync()
                .ConfigureAwait(false);

            return expired;
        }

        /// <inheritdoc/>
        public async Task<bool> ExistsAsync(string entityType, Guid entityId)
        {
            // Existenz-Check ohne Blob-Zugriff: prüft nur ob ein Eintrag mit Daten existiert.
            // ImageData.Length im Predicate kann je nach EF-Core-Version/Provider zu
            // Übersetzungsproblemen führen, daher wird die Länge per Projektion ermittelt.
            int? length = await _context.CoverImages
                .AsNoTracking()
                .Where(c => c.EntityType == entityType && c.EntityId == entityId)
                .Select(c => (int?)c.ImageData.Length)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            return length is > 0;
        }

        /// <inheritdoc/>
        public async Task<int> ClearAllAsync()
        {
            int deleted = await _context.CoverImages.ExecuteDeleteAsync().ConfigureAwait(false);

            if (deleted > 0)
            {
                _logger.Info($"Cover-Cache vollständig geleert ({deleted} Einträge).");
            }

            return deleted;
        }

        /// <inheritdoc/>
        public async Task<int> DeleteOnlineEpisodeCoversAsync()
        {
            // Cover von reinen Online-Episoden (kein lokaler Ordner) löschen
            int deleted = await _context.Database.ExecuteSqlRawAsync("""
                DELETE FROM CoverImages
                WHERE EntityType = 'Episode'
                  AND EntityId IN (
                      SELECT e.Id FROM Episodes e
                      INNER JOIN Series s ON e.SeriesId = s.Id
                      WHERE s.IsOnlineImported = 1
                        AND (e.LocalFolderPath IS NULL OR e.LocalFolderPath = '')
                        AND e.IsDeleted = 0
                  )
                """).ConfigureAwait(false);

            return deleted;
        }
    }
}
