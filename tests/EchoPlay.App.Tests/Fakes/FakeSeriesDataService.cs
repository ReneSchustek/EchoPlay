using EchoPlay.App.Tests.Helpers;
using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ISeriesDataService"/>.
    /// Verwendet eine In-Memory-Liste als Datenspeicher.
    /// </summary>
    internal sealed class FakeSeriesDataService : ISeriesDataService
    {
        private readonly List<Series> _series = [];
        private int _nextId;

        /// <summary>Alle bisher gespeicherten Serien.</summary>
        public IReadOnlyList<Series> All => _series;

        /// <inheritdoc/>
        public Task<IReadOnlyList<Series>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Series> result = _series.OrderBy(s => s.Title).ToList();
            return Task.FromResult(result);
        }

        /// <inheritdoc/>
        public Task<Series?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Series? result = _series.FirstOrDefault(s => s.Id == id);
            return Task.FromResult(result);
        }

        /// <inheritdoc/>
        public Task<Series?> GetBySpotifyArtistIdAsync(string spotifyArtistId, CancellationToken cancellationToken = default)
        {
            Series? result = _series.FirstOrDefault(s => s.SpotifyArtistId == spotifyArtistId);
            return Task.FromResult(result);
        }

        /// <inheritdoc/>
        public Task<Series?> GetByAppleMusicArtistIdAsync(string appleMusicArtistId, CancellationToken cancellationToken = default)
        {
            Series? result = _series.FirstOrDefault(s => s.AppleMusicArtistId == appleMusicArtistId);
            return Task.FromResult(result);
        }

        /// <inheritdoc/>
        public Task AddAsync(Series series, CancellationToken cancellationToken = default)
        {
            // EF Core setzt die Id nach SaveChanges via store-generated value.
            // Im Fake übernehmen wir das per Reflection, da Id protected set hat.
            PropertyInfo idProp = typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!;
            idProp.SetValue(series, TestIds.Indexed(100 + _nextId++));
            _series.Add(series);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task UpdateAsync(Series series, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _ = _series.RemoveAll(s => s.Id == id);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<Series>> GetSubscribedAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Series> result = _series.Where(s => s.IsSubscribed).OrderBy(s => s.Title).ToList();
            return Task.FromResult(result);
        }

        /// <inheritdoc/>
        public Task SetSubscribedAsync(Guid seriesId, bool isSubscribed, CancellationToken cancellationToken = default)
        {
            Series? series = _series.FirstOrDefault(s => s.Id == seriesId);

            if (series is not null)
            {
                series.IsSubscribed = isSubscribed;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<Series>> GetFavoritesAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Series> result = _series.Where(s => s.IsFavorite).OrderBy(s => s.Title).ToList();
            return Task.FromResult(result);
        }

        /// <inheritdoc/>
        public Task SetFavoriteAsync(Guid seriesId, bool isFavorite, CancellationToken cancellationToken = default)
        {
            Series? series = _series.FirstOrDefault(s => s.Id == seriesId);

            if (series is not null)
            {
                series.IsFavorite = isFavorite;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task SetWatchedAsync(Guid seriesId, bool isWatched, CancellationToken cancellationToken = default)
        {
            Series? series = _series.FirstOrDefault(s => s.Id == seriesId);

            if (series is not null)
            {
                series.IsWatched = isWatched;
            }

            return Task.CompletedTask;
        }

    }
}
