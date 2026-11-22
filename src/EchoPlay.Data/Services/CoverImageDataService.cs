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
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace EchoPlay.Data.Services
{
    /// <summary>
    /// EF-Core-basierte Implementierung für Cover-Binärdaten.
    /// Alle Blob-Zugriffe laufen über diese Klasse, Metadaten-Services laden nie Bilddaten.
    /// </summary>
    public sealed class CoverImageDataService(
        EchoPlayDbContext context,
        EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory) : ICoverImageDataService
    {
        private readonly EchoPlayDbContext _context = context;
        private readonly ILogger _logger = loggerFactory.CreateLogger("CoverImageDataService");

        /// <inheritdoc/>
        /// <param name="entityType">Art der Entität, zu der das Cover gehört: <c>Series</c> oder <c>Episode</c>.</param>
        /// <param name="entityId">Datenbank-ID der Entität, zu der das Cover gehört.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<CoverImage?> GetByEntityAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default)
        {
            return await _context.CoverImages
                .FirstOrDefaultAsync(ByEntity(entityType, entityId), cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyDictionary<Guid, byte[]>> GetImageDataByEntitiesAsync(
            string entityType, IReadOnlyList<Guid> entityIds, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entityIds);

            if (entityIds.Count == 0) return new Dictionary<Guid, byte[]>(0);

            // Ein einziger Query mit WHERE EntityId IN (...), kein N+1
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
        /// <param name="entityType">Art der Entität, zu der das Cover gehört: <c>Series</c> oder <c>Episode</c>.</param>
        /// <param name="entityId">Datenbank-ID der Entität, zu der das Cover gehört.</param>
        /// <param name="imageData">Die Bilddaten des Covers.</param>
        /// <param name="sourceUrl">Adresse, von der das Cover stammt; <see langword="null"/>, wenn es aus einer lokalen Datei kommt.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task SetCoverAsync(string entityType, Guid entityId, byte[] imageData, Uri? sourceUrl = null, CancellationToken cancellationToken = default)
        {
            // Upsert: vorhandenes Cover ändern, sonst einfügen. Legt ein paralleler Scope
            // (z.B. Splash und Hintergrunddienst) im selben Moment an, fängt der UNIQUE-Index
            // das ab, und das Einfügen wird als Änderung wiederholt.
            CoverImage cover = new()
            {
                EntityType = entityType,
                EntityId = entityId,
                ImageData = imageData,
                SourceHash = ComputeHash(imageData),
                SourceUrl = sourceUrl?.OriginalString,
                LastChecked = EntityClock.Current.UtcNow
            };

            if (!await TryUpdateExistingAsync(cover, cancellationToken).ConfigureAwait(false))
            {
                try
                {
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
                    _ = await TryUpdateExistingAsync(cover, cancellationToken).ConfigureAwait(false);
                }
            }

            _ = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.Debug(() => $"Cover gespeichert: {entityType} {entityId}");
        }

        /// <summary>
        /// Überträgt Bild, Prüfsumme, Herkunft und Prüfzeit auf ein vorhandenes Cover, noch ohne zu
        /// speichern. Eine bekannte Herkunft bleibt stehen, wenn das neue Bild keine mitbringt.
        /// </summary>
        /// <returns><see langword="true"/>, wenn es das Cover schon gab.</returns>
        private async Task<bool> TryUpdateExistingAsync(CoverImage cover, CancellationToken cancellationToken)
        {
            CoverImage? existing = await _context.CoverImages
                .AsTracking()
                .FirstOrDefaultAsync(ByEntity(cover.EntityType, cover.EntityId), cancellationToken)
                .ConfigureAwait(false);
            if (existing is null)
            {
                return false;
            }

            existing.ImageData = cover.ImageData;
            existing.SourceHash = cover.SourceHash;
            existing.SourceUrl = cover.SourceUrl ?? existing.SourceUrl;
            existing.LastChecked = cover.LastChecked;
            return true;
        }

        /// <inheritdoc/>
        /// <param name="entityType">Art der Entität, zu der das Cover gehört: <c>Series</c> oder <c>Episode</c>.</param>
        /// <param name="entityId">Datenbank-ID der Entität, zu der das Cover gehört.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<bool> ExistsAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default)
        {
            // Existenz-Check ohne Blob-Zugriff: prüft nur ob ein Eintrag mit Daten existiert.
            // ImageData.Length im Predicate kann je nach EF-Core-Version/Provider zu
            // Übersetzungsproblemen führen, daher wird die Länge per Projektion ermittelt.
            int? length = await _context.CoverImages
                .Where(ByEntity(entityType, entityId))
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
        /// <param name="entityType">Art der Entität, zu der das Cover gehört: <c>Series</c> oder <c>Episode</c>.</param>
        /// <param name="entityIds">Datenbank-IDs der Entitäten, deren Cover entfernt werden.</param>
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
        /// Prädikat für den Einzel-Lookup eines Covers über EntityType und EntityId.
        /// Zentralisiert die sonst mehrfach wiederholte Filterbedingung.
        /// </summary>
        /// <param name="entityType">Der Entitätstyp (z.B. "Episode", "Series").</param>
        /// <param name="entityId">Die ID der Entität.</param>
        /// <returns>Ein EF-übersetzbares Filterprädikat.</returns>
        private static Expression<Func<CoverImage, bool>> ByEntity(string entityType, Guid entityId) =>
            cover => cover.EntityType == entityType && cover.EntityId == entityId;

        /// <summary>
        /// Berechnet den SHA-256-Hash der Bilddaten als Hex-String (64 Zeichen).
        /// </summary>
        /// <param name="data">Die Bilddaten, über die der Hash gebildet wird.</param>
        private static string ComputeHash(byte[] data)
        {
            // SHA-256 liefert 32 Byte.
            Span<byte> hash = stackalloc byte[32];
            _ = SHA256.HashData(data, hash);
            return Convert.ToHexString(hash);
        }
    }
}
