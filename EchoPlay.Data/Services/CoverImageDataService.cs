using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Infrastructure;
using EchoPlay.Data.Internal;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
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
        /// <param name="entityType">Parameter entityType.</param>
        /// <param name="entityId">Parameter entityId.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<CoverImage?> GetByEntityAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default)
        {
            return await _context.CoverImages

                .FirstOrDefaultAsync(c => c.EntityType == entityType && c.EntityId == entityId, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyDictionary<Guid, byte[]>> GetImageDataByEntitiesAsync(
            string entityType, IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entityIds);

            if (entityIds.Count == 0) return new Dictionary<Guid, byte[]>(0);

            // Ein einziger Query mit WHERE EntityId IN (...) – kein N+1
            List<CoverImage> covers = await _context.CoverImages

                .Where(c => c.EntityType == entityType && entityIds.Contains(c.EntityId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // Platzhalter-Einträge (ImageData leer, nur LastChecked) ausfiltern,
            // damit sie nicht fälschlich als vorhandenes Cover gewertet werden
            Dictionary<Guid, byte[]> result = new(covers.Count);

            foreach (CoverImage cover in covers)
            {
                if (cover.ImageData.Length > 0)
                {
                    result[cover.EntityId] = cover.ImageData;
                }
            }

            return result;
        }

        /// <inheritdoc/>
        /// <param name="entityType">Parameter entityType.</param>
        /// <param name="entityId">Parameter entityId.</param>
        /// <param name="imageData">Parameter imageData.</param>
        /// <param name="sourceUrl">Parameter sourceUrl.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task SetCoverAsync(string entityType, Guid entityId, byte[] imageData, string? sourceUrl = null, CancellationToken cancellationToken = default)
        {
            // Upsert per SQL: INSERT OR REPLACE vermeidet Race Conditions zwischen
            // parallelen Scopes (z.B. RunOnceAsync im Splash + Start im Hintergrund).
            DateTime now = EntityClock.Current.UtcNow;
            string hash = ComputeHash(imageData);

            CoverImage? existing = await _context.CoverImages
                .AsTracking()
                .FirstOrDefaultAsync(c => c.EntityType == entityType && c.EntityId == entityId, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                existing.ImageData = imageData;
                existing.SourceHash = hash;
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
                        SourceHash = hash,
                        SourceUrl = sourceUrl,
                        LastChecked = now
                    };
                    _ = _context.CoverImages.Add(cover);
                    _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    _logger.Debug(() => $"Cover gespeichert: {entityType} {entityId}");
                    return;
                }
                catch (DbUpdateException ex) when (UniqueConstraintHandler.IsUniqueViolation(ex))
                {
                    // UNIQUE-Konflikt: ein anderer Scope hat parallel eingefügt.
                    // Change-Tracker zurücksetzen und als Update wiederholen.
                    _context.ChangeTracker.Clear();

                    CoverImage? retry = await _context.CoverImages
                        .AsTracking()
                        .FirstOrDefaultAsync(c => c.EntityType == entityType && c.EntityId == entityId, cancellationToken)
                        .ConfigureAwait(false);

                    if (retry is not null)
                    {
                        retry.ImageData = imageData;
                        retry.SourceHash = hash;
                        retry.SourceUrl = sourceUrl ?? retry.SourceUrl;
                        retry.LastChecked = now;
                    }
                }
            }

            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.Debug(() => $"Cover gespeichert: {entityType} {entityId}");
        }

        /// <inheritdoc/>
        /// <param name="entityType">Parameter entityType.</param>
        /// <param name="entityId">Parameter entityId.</param>
        /// <param name="checkedAt">Parameter checkedAt.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task SetLastCheckedAsync(string entityType, Guid entityId, DateTime checkedAt, CancellationToken cancellationToken = default)
        {
            CoverImage? existing = await _context.CoverImages
                .AsTracking()
                .FirstOrDefaultAsync(c => c.EntityType == entityType && c.EntityId == entityId, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                existing.LastChecked = checkedAt;
                _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
                _ = _context.CoverImages.Add(placeholder);
                _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<Guid>> GetUncheckedEntityIdsAsync(
            string entityType, DateTime cooldownThreshold, int limit, CancellationToken cancellationToken = default)
        {
            // Entities die entweder noch nie geprüft wurden (kein Eintrag in CoverImages)
            // oder deren LastChecked abgelaufen ist UND kein Cover haben
            // Für Serien: alle Series-IDs ohne Cover-Eintrag oder mit abgelaufenem Check
            // Für Episoden: alle Episode-IDs ohne Cover-Eintrag oder mit abgelaufenem Check

            // Entities MIT abgelaufenem Check und OHNE Bild
            List<Guid> expired = await _context.CoverImages

                .Where(c => c.EntityType == entityType
                    && c.ImageData.Length == 0
                    && (c.LastChecked == null || c.LastChecked < cooldownThreshold))
                .Select(c => c.EntityId)
                .Take(limit)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return expired;
        }

        /// <inheritdoc/>
        /// <param name="entityType">Parameter entityType.</param>
        /// <param name="entityId">Parameter entityId.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<bool> ExistsAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default)
        {
            // Existenz-Check ohne Blob-Zugriff: prüft nur ob ein Eintrag mit Daten existiert.
            // ImageData.Length im Predicate kann je nach EF-Core-Version/Provider zu
            // Übersetzungsproblemen führen, daher wird die Länge per Projektion ermittelt.
            int? length = await _context.CoverImages

                .Where(c => c.EntityType == entityType && c.EntityId == entityId)
                .Select(c => (int?)c.ImageData.Length)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return length is > 0;
        }

        /// <inheritdoc/>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<int> ClearAllAsync(CancellationToken cancellationToken = default)
        {
            int deleted = await _context.CoverImages.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            if (deleted > 0)
            {
                _logger.Info("Cover-Cache vollständig geleert ({DeletedCount} Einträge).", deleted);
            }

            return deleted;
        }

        /// <inheritdoc/>
        /// <param name="entityType">Parameter entityType.</param>
        /// <param name="entityIds">Parameter entityIds.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<int> DeleteByEntitiesAsync(string entityType, IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entityIds);

            if (entityIds.Count == 0) return 0;

            int deleted = await _context.CoverImages
                .Where(c => c.EntityType == entityType && entityIds.Contains(c.EntityId))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                _logger.Debug(() => $"{deleted} Cover-Einträge für {entityType} entfernt.");
            }

            return deleted;
        }

        /// <inheritdoc/>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public Task<int> CountAsync(CancellationToken cancellationToken = default) => _context.CoverImages.CountAsync(cancellationToken);

        /// <summary>
        /// Berechnet den SHA-256-Hash der Bilddaten als Hex-String (64 Zeichen).
        /// </summary>
        /// <param name="data">Parameter data.</param>
        private static string ComputeHash(byte[] data)
        {
            Span<byte> hash = stackalloc byte[32];
            _ = SHA256.HashData(data, hash);
            return Convert.ToHexString(hash);
        }
    }
}
