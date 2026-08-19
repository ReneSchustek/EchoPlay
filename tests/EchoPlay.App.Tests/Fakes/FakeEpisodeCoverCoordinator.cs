using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IEpisodeCoverCoordinator"/>.
    /// Hält Suchanfragen und übernommene Cover fest, ohne Bilder zu laden oder zu schreiben.
    /// </summary>
    internal sealed class FakeEpisodeCoverCoordinator : IEpisodeCoverCoordinator
    {
        /// <summary>Alle Suchanfragen als (Begriff, Abschnitt).</summary>
        public List<(string Query, EchoPlay.LocalLibrary.Cover.CoverSearchPage Page)> SearchCalls { get; } = [];

        /// <summary>Treffer, die <see cref="SearchCoversAsync"/> liefert.</summary>
        public IReadOnlyList<CoverSearchHit> Hits { get; set; } = [];

        /// <summary>Serien-Cover, die aus rohen Bytes übernommen wurden.</summary>
        public List<(LocalArtistCardViewModel Card, byte[] Bytes)> SeriesCoverBytes { get; } = [];

        /// <summary>Folgen-Cover, die aus rohen Bytes übernommen wurden.</summary>
        public List<(LocalEpisodeCardViewModel Card, byte[] Bytes)> EpisodeCoverBytes { get; } = [];

        /// <summary>Über die Trefferauswahl übernommene Serien-Cover.</summary>
        public List<(LocalArtistCardViewModel Card, CoverSearchHit Hit)> SelectedSeriesCovers { get; } = [];

        /// <summary>Über die Trefferauswahl übernommene Folgen-Cover.</summary>
        public List<(LocalEpisodeCardViewModel Card, CoverSearchHit Hit)> SelectedEpisodeCovers { get; } = [];

        /// <inheritdoc/>
        public Task<IReadOnlyList<CoverSearchHit>> SearchCoversAsync(
            string query, EchoPlay.LocalLibrary.Cover.CoverSearchPage page, CancellationToken ct)
        {
            SearchCalls.Add((query, page));
            return Task.FromResult(Hits);
        }

        /// <inheritdoc/>
        public Task ApplySeriesCoverFromBytesAsync(
            LocalArtistCardViewModel card, byte[] bytes, CancellationToken cancellationToken = default)
        {
            SeriesCoverBytes.Add((card, bytes));
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task ApplyEpisodeCoverFromBytesAsync(
            LocalEpisodeCardViewModel card, byte[] bytes, CancellationToken cancellationToken = default)
        {
            EpisodeCoverBytes.Add((card, bytes));
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task ApplySelectedSeriesCoverAsync(
            LocalArtistCardViewModel card, CoverSearchHit hit, CancellationToken cancellationToken = default)
        {
            SelectedSeriesCovers.Add((card, hit));
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task ApplySelectedEpisodeCoverAsync(
            LocalEpisodeCardViewModel card, CoverSearchHit hit, CancellationToken cancellationToken = default)
        {
            SelectedEpisodeCovers.Add((card, hit));
            return Task.CompletedTask;
        }
    }
}
