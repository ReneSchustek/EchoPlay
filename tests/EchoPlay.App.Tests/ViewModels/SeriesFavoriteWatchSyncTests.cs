using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft, dass die Kachel-ViewModels das Auge-Symbol sofort mitziehen, wenn Favorisieren
    /// in der Datenschicht die Überwachung aktiviert. Ohne diesen Gleichlauf zeigte die Kachel
    /// bis zum nächsten Laden einen anderen Stand als die Datenbank.
    /// </summary>
    public sealed class SeriesFavoriteWatchSyncTests
    {
        private static async Task<(IServiceScopeFactory ScopeFactory, Guid SeriesId)> BuildScopeAsync()
        {
            FakeSeriesDataService series = new();
            Series s = new() { Title = "TKKG" };
            await series.AddAsync(s, TestContext.Current.CancellationToken);

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => series);
            ServiceProvider provider = services.BuildServiceProvider();

            return (provider.GetRequiredService<IServiceScopeFactory>(), s.Id);
        }

        [Fact]
        public async Task LocalArtistCard_ToggleFavoriteOn_SetsWatchedOnCard()
        {
            (IServiceScopeFactory scopeFactory, Guid seriesId) = await BuildScopeAsync();

            LocalArtistCardViewModel card = new(
                seriesId: seriesId,
                title: "TKKG",
                coverImage: null,
                localFolderPath: null,
                localEpisodeCount: 0,
                totalEpisodeCount: 0,
                isFavorite: false,
                isWatched: false,
                scopeFactory: scopeFactory);

            card.ToggleFavoriteCommand.Execute(null);
            await ChangeSignals.WaitForAsync(card, () => card.IsWatched, "Kachel zieht die Überwachung nach");

            Assert.True(card.IsFavorite);
            Assert.True(card.IsWatched);
        }

        [Fact]
        public async Task SeriesCard_ToggleFavoriteOn_SetsWatchedOnCard()
        {
            (IServiceScopeFactory scopeFactory, Guid seriesId) = await BuildScopeAsync();

            SeriesCardViewModel card = new(
                id: seriesId,
                title: "TKKG",
                coverImage: null,
                totalEpisodeCount: 0,
                newEpisodeCount: 0,
                inProgressCount: 0,
                finishedCount: 0,
                isSubscribed: true,
                isFavorite: false,
                isWatched: false,
                scopeFactory: scopeFactory,
                confirmationDialogService: new FakeConfirmationDialogService(),
                localizationService: new FakeLocalizationService());

            card.ToggleFavoriteCommand.Execute(null);
            await ChangeSignals.WaitForAsync(card, () => card.IsWatched, "Kachel zieht die Überwachung nach");

            Assert.True(card.IsFavorite);
            Assert.True(card.IsWatched);
        }

        [Fact]
        public async Task LocalArtistCard_ToggleFavoriteOff_LeavesWatchedUntouched()
        {
            // Entfavorisieren darf die Überwachung nicht mit abschalten – das bleibt das Auge.
            (IServiceScopeFactory scopeFactory, Guid seriesId) = await BuildScopeAsync();

            LocalArtistCardViewModel card = new(
                seriesId: seriesId,
                title: "TKKG",
                coverImage: null,
                localFolderPath: null,
                localEpisodeCount: 0,
                totalEpisodeCount: 0,
                isFavorite: true,
                isWatched: true,
                scopeFactory: scopeFactory);

            card.ToggleFavoriteCommand.Execute(null);
            await ChangeSignals.WaitForAsync(card, () => !card.IsFavorite, "Favorit wird abgeschaltet");

            Assert.False(card.IsFavorite);
            Assert.True(card.IsWatched);
        }
    }
}
