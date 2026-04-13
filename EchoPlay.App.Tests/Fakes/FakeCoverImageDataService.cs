using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ICoverImageDataService"/> zur Verwendung in Tests.
    /// Speichert Cover in einem Dictionary, ohne Datenbankzugriff.
    /// </summary>
    internal sealed class FakeCoverImageDataService : ICoverImageDataService
    {
        /// <summary>
        /// Gespeicherte Cover (Key = EntityType + EntityId).
        /// </summary>
        private readonly Dictionary<(string EntityType, Guid EntityId), CoverImage> _covers = [];

        /// <inheritdoc/>
        public Task<CoverImage?> GetByEntityAsync(string entityType, Guid entityId)
        {
            _ = _covers.TryGetValue((entityType, entityId), out CoverImage? cover);
            return Task.FromResult(cover);
        }

        /// <inheritdoc/>
        public Task<IReadOnlyDictionary<Guid, byte[]>> GetImageDataByEntitiesAsync(
            string entityType, IReadOnlyList<Guid> entityIds)
        {
            Dictionary<Guid, byte[]> result = new();

            foreach (Guid id in entityIds)
            {
                if (_covers.TryGetValue((entityType, id), out CoverImage? cover)
                    && cover.ImageData is { Length: > 0 })
                {
                    result[id] = cover.ImageData;
                }
            }

            return Task.FromResult<IReadOnlyDictionary<Guid, byte[]>>(result);
        }

        /// <inheritdoc/>
        public Task SetCoverAsync(string entityType, Guid entityId, byte[] imageData, string? sourceUrl = null)
        {
            CoverImage cover = new()
            {
                EntityType = entityType,
                EntityId = entityId,
                ImageData = imageData,
                SourceUrl = sourceUrl
            };

            _covers[(entityType, entityId)] = cover;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task SetLastCheckedAsync(string entityType, Guid entityId, DateTime checkedAt)
        {
            if (_covers.TryGetValue((entityType, entityId), out CoverImage? cover))
            {
                cover.LastChecked = checkedAt;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<Guid>> GetUncheckedEntityIdsAsync(
            string entityType, DateTime cooldownThreshold, int limit)
        {
            List<Guid> result = _covers
                .Where(kv => kv.Key.EntityType == entityType
                    && (!kv.Value.LastChecked.HasValue || kv.Value.LastChecked.Value < cooldownThreshold))
                .Select(kv => kv.Key.EntityId)
                .Take(limit)
                .ToList();

            return Task.FromResult<IReadOnlyList<Guid>>(result);
        }

        /// <inheritdoc/>
        public Task<bool> ExistsAsync(string entityType, Guid entityId)
        {
            bool exists = _covers.ContainsKey((entityType, entityId));
            return Task.FromResult(exists);
        }

        /// <inheritdoc/>
        public Task<int> ClearAllAsync()
        {
            int count = _covers.Count;
            _covers.Clear();
            return Task.FromResult(count);
        }

    }
}
