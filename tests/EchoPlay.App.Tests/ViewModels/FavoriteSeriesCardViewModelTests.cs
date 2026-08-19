using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Favoritenkachel der Startseite: die Rückfrage vor dem Entfernen, die
    /// Wirkung auf den Bestand und die Meldung, mit der die Übersicht die Kachel abräumt.
    /// </summary>
    public sealed class FavoriteSeriesCardViewModelTests
    {
        private const string SeriesName = "Die drei Fragezeichen";

        [Fact]
        public async Task RemoveFromFavorites_WhenConfirmed_DropsTheSeriesFromTheFavorites()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = SeriesName, IsFavorite = true, IsWatched = true };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FavoriteSeriesCardViewModel sut = BuildCard(seriesService, series.Id, confirm: true);
            TaskCompletionSource<Guid> removed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            sut.RemovedFromFavorites += id => removed.TrySetResult(id);

            sut.RemoveFromFavoritesCommand.Execute(null);

            Guid announced = await removed.Task.WaitAsync(
                TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.Equal(series.Id, announced);
            Series? stored = await seriesService.GetByIdAsync(series.Id, TestContext.Current.CancellationToken);
            Assert.NotNull(stored);
            Assert.False(stored.IsFavorite);
        }

        [Fact]
        public async Task RemoveFromFavorites_WhenDeclined_KeepsTheSeriesAsFavorite()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = SeriesName, IsFavorite = true };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            FakeConfirmationDialogService dialog = new(result: false);
            FavoriteSeriesCardViewModel sut = BuildCard(seriesService, series.Id, dialog);

            sut.RemoveFromFavoritesCommand.Execute(null);

            // Abgelehnt heißt abgelehnt: Ein Klick daneben darf die Sammlung nicht ändern.
            Assert.Equal(1, dialog.CallCount);
            Series? stored = await seriesService.GetByIdAsync(series.Id, TestContext.Current.CancellationToken);
            Assert.NotNull(stored);
            Assert.True(stored.IsFavorite);
        }

        [Fact]
        public void RemoveFromFavorites_NamesTheSeriesInTheQuestion()
        {
            FakeConfirmationDialogService dialog = new(result: false);
            FavoriteSeriesCardViewModel sut = BuildCard(new FakeSeriesDataService(), Guid.Empty, dialog);

            sut.RemoveFromFavoritesCommand.Execute(null);

            // Bei einer Wand aus Kacheln muss die Rückfrage sagen, welche gemeint ist.
            Assert.NotNull(dialog.LastMessage);
            Assert.Contains(SeriesName, dialog.LastMessage, StringComparison.Ordinal);
        }

        [Fact]
        public void ActionsAutomationName_NamesTheSeries()
        {
            FavoriteSeriesCardViewModel sut = BuildCard(new FakeSeriesDataService(), Guid.Empty, confirm: true);

            // Die Vorlesehilfe liest sonst bei jeder Kachel denselben Text vor.
            Assert.Contains(SeriesName, sut.ActionsAutomationName, StringComparison.Ordinal);
        }

        [Fact]
        public void CoverImage_WithoutPicture_StaysEmpty()
        {
            FavoriteSeriesCardViewModel sut = BuildCard(new FakeSeriesDataService(), Guid.Empty, confirm: true);

            Assert.Null(sut.CoverImage);
        }

        private static FavoriteSeriesCardViewModel BuildCard(
            FakeSeriesDataService seriesService, Guid seriesId, bool confirm)
            => BuildCard(seriesService, seriesId, new FakeConfirmationDialogService(confirm));

        private static FavoriteSeriesCardViewModel BuildCard(
            FakeSeriesDataService seriesService,
            Guid seriesId,
            FakeConfirmationDialogService dialog)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            ServiceProvider provider = services.BuildServiceProvider();

            return new FavoriteSeriesCardViewModel(
                seriesId,
                SeriesName,
                coverImage: null,
                provider.GetRequiredService<IServiceScopeFactory>(),
                dialog,
                new FakeLocalizationService(new Dictionary<string, string>
                {
                    ["FavoriteRemoveTitle"] = "Aus Favoriten entfernen?",
                    ["FavoriteRemoveMessage"] = "{0} wird nicht mehr als Favorit angezeigt.",
                    ["TileActionsAutomationName"] = "Weitere Aktionen: {0}",
                }));
        }
    }
}
