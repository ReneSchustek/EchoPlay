using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft den Favoriten-Abschnitt der Startseite, wenn eine Serie entfernt wird.
    /// </summary>
    /// <remarks>
    /// Die Kachel meldet das Entfernen selbst. Hört der Abschnitt nicht zu, bliebe sie
    /// stehen, bis der Anwender die Seite neu betritt — und ein Klick auf die Kachel
    /// führte zu einer Serie, die längst nicht mehr in den Favoriten steht.
    /// </remarks>
    public sealed class DashboardFavoritenRemovalTests
    {
        [Fact]
        public void SetItems_WithEntries_ShowsTheSection()
        {
            DashboardFavoritenViewModel sut = new(BuildScopeFactory(new FakeSeriesDataService()), new FakeLogger());

            sut.SetItems([BuildCard(new FakeSeriesDataService(), Guid.Empty)]);

            Assert.Equal(Visibility.Visible, sut.FavoriteSectionVisibility);
            _ = Assert.Single(sut.FavoriteSeries);
            sut.Dispose();
        }

        [Fact]
        public void SetItems_WithoutEntries_HidesTheSection()
        {
            DashboardFavoritenViewModel sut = new(BuildScopeFactory(new FakeSeriesDataService()), new FakeLogger());

            sut.SetItems([]);

            Assert.Equal(Visibility.Collapsed, sut.FavoriteSectionVisibility);
            sut.Dispose();
        }

        [Fact]
        public void SetItems_AnnouncesTheChangeToTheDashboard()
        {
            DashboardFavoritenViewModel sut = new(BuildScopeFactory(new FakeSeriesDataService()), new FakeLogger());
            int announced = 0;
            sut.FavoritesChanged += () => announced++;

            sut.SetItems([BuildCard(new FakeSeriesDataService(), Guid.Empty)]);

            Assert.Equal(1, announced);
            sut.Dispose();
        }

        [Fact]
        public async Task WhenACardReportsItsRemoval_TheTileDisappears()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "Die drei Fragezeichen", IsFavorite = true };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            DashboardFavoritenViewModel sut = new(BuildScopeFactory(seriesService), new FakeLogger());
            FavoriteSeriesCardViewModel card = BuildCard(seriesService, series.Id);
            sut.SetItems([card]);

            TaskCompletionSource removed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            sut.FavoritesChanged += () => removed.TrySetResult();

            card.RemoveFromFavoritesCommand.Execute(null);
            await removed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.Empty(sut.FavoriteSeries);
            Assert.Equal(Visibility.Collapsed, sut.FavoriteSectionVisibility);
            sut.Dispose();
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static IServiceScopeFactory BuildScopeFactory(FakeSeriesDataService seriesService)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IDashboardPositionDataService>(_ => new FakeDashboardPositionDataService());
            ServiceProvider provider = services.BuildServiceProvider();
            return provider.GetRequiredService<IServiceScopeFactory>();
        }

        private static FavoriteSeriesCardViewModel BuildCard(FakeSeriesDataService seriesService, Guid seriesId)
            => new(
                seriesId,
                "Die drei Fragezeichen",
                coverImage: null,
                BuildScopeFactory(seriesService),
                new FakeConfirmationDialogService(result: true),
                new FakeLocalizationService(new Dictionary<string, string>
                {
                    ["FavoriteRemoveTitle"] = "Aus Favoriten entfernen?",
                    ["FavoriteRemoveMessage"] = "{0} wird nicht mehr als Favorit angezeigt.",
                    ["TileActionsAutomationName"] = "Weitere Aktionen: {0}",
                }));
    }
}
