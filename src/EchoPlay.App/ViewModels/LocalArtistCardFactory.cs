using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Baut die Serienkacheln der lokalen Mediathek: Bestand aus der Datenbank, Folgenzähler
    /// und Cover. Getrennt vom Ansichtsmodell, weil „woher kommen die Karten" und „welche
    /// zeigt die Seite gerade" zwei verschiedene Fragen sind.
    /// </summary>
    internal sealed class LocalArtistCardFactory
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISeriesCoverBuilder? _coverBuilder;

        /// <summary>
        /// Initialisiert die Fabrik.
        /// </summary>
        /// <param name="scopeFactory">Für die Datenbankzugriffe.</param>
        /// <param name="coverBuilder">Beschafft die Cover. Ohne ihn bleiben die Kacheln bildlos.</param>
        public LocalArtistCardFactory(IServiceScopeFactory scopeFactory, ISeriesCoverBuilder? coverBuilder)
        {
            _scopeFactory = scopeFactory;
            _coverBuilder = coverBuilder;
        }

        /// <summary>
        /// Lädt alle Serien mit lokalem Ordner und baut daraus Kacheln.
        /// Die Cover kommen im Hintergrund nach — die Kacheln stehen sofort.
        /// </summary>
        /// <returns>Die Kacheln in der Reihenfolge, in der die Datenbank die Serien liefert.</returns>
        public async Task<IReadOnlyList<LocalArtistCardViewModel>> CreateAllAsync()
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();

            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync();
            List<Series> localSeries = [.. allSeries.Where(series => series.LocalFolderPath is not null)];

            // Folgenzähler in einer Sammelabfrage – ersetzt einen Aufruf je Serie
            IReadOnlyDictionary<Guid, (int Total, int Local)> episodeCounts =
                await episodeService.GetEpisodeCountsForSeriesAsync([.. localSeries.Select(series => series.Id)]);

            List<LocalArtistCardViewModel> cards = new(localSeries.Count);

            foreach (Series series in localSeries)
            {
                (int total, int local) = episodeCounts.TryGetValue(series.Id, out (int Total, int Local) counts)
                    ? (counts.Total, counts.Local)
                    : (0, 0);

                cards.Add(BuildCard(series, local, total, cover: null));
            }

            // Cover nachladen, während die Kacheln schon stehen
            foreach ((LocalArtistCardViewModel card, Series series) in cards.Zip(localSeries))
            {
                _ = FillCoverAsync(card, series);
            }

            return cards;
        }

        /// <summary>
        /// Baut eine einzelne Kachel samt Zählern und Cover — für Serien, die während eines
        /// Einlesevorgangs gemeldet werden.
        /// </summary>
        /// <param name="series">Die gemeldete Serie.</param>
        /// <returns>Die fertige Kachel.</returns>
        public async Task<LocalArtistCardViewModel> CreateAsync(Series series)
        {
            ArgumentNullException.ThrowIfNull(series);

            (int local, int total) = await CountEpisodesAsync(series.Id);
            BitmapImage? cover = _coverBuilder is null ? null : await _coverBuilder.BuildAsync(series);

            return BuildCard(series, local, total, cover);
        }

        /// <summary>
        /// Zählt die Folgen einer Serie: insgesamt und davon lokal vorhanden.
        /// </summary>
        /// <param name="seriesId">Kennung der Serie.</param>
        /// <returns>Die beiden Zähler.</returns>
        public async Task<(int LocalCount, int TotalCount)> CountEpisodesAsync(Guid seriesId)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();

            IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(seriesId);

            return (episodes.Count(episode => episode.LocalFolderPath is not null), episodes.Count);
        }

        /// <summary>
        /// Trägt das Cover einer Kachel nach, die noch keines hat. Ein vorhandenes Bild bleibt
        /// unangetastet — sonst flackerte die Kachel und die Datenbank würde ohne Grund gefragt.
        /// </summary>
        /// <param name="card">Die Kachel.</param>
        /// <param name="series">Die zugehörige Serie.</param>
        /// <returns>Der Task ist abgeschlossen, wenn das Cover steht oder feststeht, dass es keines gibt.</returns>
        public async Task FillMissingCoverAsync(LocalArtistCardViewModel card, Series series)
        {
            ArgumentNullException.ThrowIfNull(card);

            if (card.CoverImage is not null || _coverBuilder is null)
            {
                return;
            }

            BitmapImage? cover = await _coverBuilder.BuildAsync(series);

            if (cover is not null)
            {
                card.CoverImage = cover;
            }
        }

        /// <summary>Beschafft das Cover im Hintergrund; Lesefehler bleiben ohne Folgen.</summary>
        private async Task FillCoverAsync(LocalArtistCardViewModel card, Series series)
        {
            if (_coverBuilder is not null)
            {
                card.CoverImage = await _coverBuilder.TryBuildAsync(series);
            }
        }

        /// <summary>Erzeugt die Kachel aus Serie, Zählern und Cover.</summary>
        private LocalArtistCardViewModel BuildCard(Series series, int localCount, int totalCount, BitmapImage? cover)
        {
            return new LocalArtistCardViewModel(
                seriesId: series.Id,
                title: series.Title,
                coverImage: cover,
                localFolderPath: series.LocalFolderPath,
                localEpisodeCount: localCount,
                totalEpisodeCount: totalCount,
                isFavorite: series.IsFavorite,
                isWatched: series.IsWatched,
                scopeFactory: _scopeFactory);
        }
    }
}
