using EchoPlay.App.Services;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ICoverService"/>. Zeichnet auf, für welche Serien-IDs ein Cover
    /// angefordert wurde, und liefert stets <see langword="null"/> zurück – ein echtes
    /// <see cref="BitmapImage"/> ist im headless Testhost (UseWinUI=false) nicht konstruierbar.
    /// Tests prüfen daher den <em>Nachlade-Versuch</em> (Aufrufzählung), nicht das Bitmap selbst.
    /// </summary>
    internal sealed class FakeCoverService : ICoverService
    {
        private readonly List<Guid> _seriesCoverRequests = [];
        private readonly List<Guid> _storedSeriesCovers = [];
        private readonly List<Guid> _storedEpisodeCovers = [];

        /// <summary>Alle Serien-IDs, für die <see cref="GetSeriesCoverImageAsync"/> aufgerufen wurde (Reihenfolge erhalten).</summary>
        public IReadOnlyList<Guid> SeriesCoverRequests => _seriesCoverRequests;

        /// <summary>Alle Serien-IDs, für die ein Cover abgelegt wurde (Reihenfolge erhalten).</summary>
        public IReadOnlyList<Guid> StoredSeriesCovers => _storedSeriesCovers;

        /// <summary>Alle Folgen-IDs, für die ein Cover abgelegt wurde (Reihenfolge erhalten).</summary>
        public IReadOnlyList<Guid> StoredEpisodeCovers => _storedEpisodeCovers;

        /// <inheritdoc/>
        public Task<BitmapImage?> GetSeriesCoverImageAsync(Guid seriesId, CancellationToken cancellationToken = default)
        {
            _seriesCoverRequests.Add(seriesId);
            return Task.FromResult<BitmapImage?>(null);
        }

        /// <inheritdoc/>
        public Task<BitmapImage?> GetEpisodeCoverImageAsync(Guid episodeId, CancellationToken cancellationToken = default)
        {
            _episodeCoverRequests.Add(episodeId);
            return Task.FromResult<BitmapImage?>(null);
        }

        private readonly List<Guid> _episodeCoverRequests = [];

        /// <summary>Jede Einzelabfrage nach einem Folgen-Cover, in Aufrufreihenfolge.</summary>
        public IReadOnlyList<Guid> EpisodeCoverRequests => _episodeCoverRequests;

        private readonly List<IReadOnlyList<Guid>> _episodeCoverBatchRequests = [];

        /// <summary>Jede Sammelabfrage nach Folgen-Covern, in Aufrufreihenfolge.</summary>
        public IReadOnlyList<IReadOnlyList<Guid>> EpisodeCoverBatchRequests => _episodeCoverBatchRequests;

        /// <inheritdoc/>
        public Task<IReadOnlyDictionary<Guid, byte[]>> GetEpisodeCoverBytesAsync(IReadOnlyList<Guid> episodeIds, CancellationToken cancellationToken = default)
        {
            _episodeCoverBatchRequests.Add(episodeIds ?? []);

            Dictionary<Guid, byte[]> found = [];

            foreach (Guid id in episodeIds ?? [])
            {
                if (ExistingEpisodeCovers.TryGetValue(id, out byte[]? bytes))
                {
                    found[id] = bytes;
                }
            }

            return Task.FromResult<IReadOnlyDictionary<Guid, byte[]>>(found);
        }

        /// <summary>
        /// Cover, die bereits in der Ablage liegen. Wer sie vorgibt, prüft den Weg, auf dem
        /// nichts nachgeladen werden muss.
        /// </summary>
        public Dictionary<Guid, byte[]> ExistingEpisodeCovers { get; } = [];

        /// <inheritdoc/>
        public Task SetSeriesCoverAsync(Guid seriesId, byte[] imageData, string? sourceUrl = null, CancellationToken cancellationToken = default)
        {
            _storedSeriesCovers.Add(seriesId);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task SetEpisodeCoverAsync(Guid episodeId, byte[] imageData, string? sourceUrl = null, CancellationToken cancellationToken = default)
        {
            _storedEpisodeCovers.Add(episodeId);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<bool> HasSeriesCoverAsync(Guid seriesId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
