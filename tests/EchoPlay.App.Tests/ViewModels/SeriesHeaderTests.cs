using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Kopfzeile der Serienansicht: Titel, Beschreibung, Favoritenstern und
    /// Gesamtfortschritt.
    /// </summary>
    /// <remarks>
    /// Alles hier gehört zur Serie als Ganzes und wird bei jedem Ladelauf gemeinsam gesetzt.
    /// Bleibt ein Teil davon stehen, zeigt die Kopfzeile eine Serie und die Liste darunter
    /// eine andere.
    /// </remarks>
    public sealed class SeriesHeaderTests
    {
        [Fact]
        public void Apply_TakesOverTitleDescriptionAndProgress()
        {
            SeriesHeader sut = Build(new FakeSeriesDataService());

            sut.Apply(Guid.Empty, new SeriesDetailData(
                new Series { Title = "TKKG", Description = "Vier Freunde" },
                Tiles: [],
                ProgressText: "42 von 229 Folgen gehört",
                ProgressPercent: 18.3));

            Assert.Equal("TKKG", sut.SeriesTitle);
            Assert.Equal("Vier Freunde", sut.SeriesDescription);
            Assert.Equal("42 von 229 Folgen gehört", sut.ProgressText);
            Assert.Equal(18.3, sut.OverallProgressPercent);
        }

        [Fact]
        public void Apply_WithoutDescription_HidesTheDescriptionArea()
        {
            SeriesHeader sut = Build(new FakeSeriesDataService());

            sut.Apply(Guid.Empty, new SeriesDetailData(
                new Series { Title = "TKKG" }, Tiles: [], ProgressText: "", ProgressPercent: 0));

            Assert.Equal(Visibility.Collapsed, sut.DescriptionVisibility);
        }

        [Fact]
        public void Apply_WithDescription_ShowsTheDescriptionArea()
        {
            SeriesHeader sut = Build(new FakeSeriesDataService());

            sut.Apply(Guid.Empty, new SeriesDetailData(
                new Series { Title = "TKKG", Description = "Vier Freunde" },
                Tiles: [], ProgressText: "", ProgressPercent: 0));

            Assert.Equal(Visibility.Visible, sut.DescriptionVisibility);
        }

        [Fact]
        public void Apply_WithoutSeries_ClearsTheHeader()
        {
            SeriesHeader sut = Build(new FakeSeriesDataService());
            sut.Apply(Guid.Empty, new SeriesDetailData(
                new Series { Title = "TKKG", Description = "Vier Freunde", IsFavorite = true },
                Tiles: [], ProgressText: "x", ProgressPercent: 5));

            sut.Apply(Guid.Empty, new SeriesDetailData(
                Series: null, Tiles: [], ProgressText: "", ProgressPercent: 0));

            // Eine nicht gefundene Serie darf nicht die Angaben der vorigen weiterzeigen.
            Assert.Equal(string.Empty, sut.SeriesTitle);
            Assert.Equal(string.Empty, sut.SeriesDescription);
            Assert.False(sut.IsFavorite);
        }

        [Fact]
        public void Apply_WithoutData_ThrowsArgumentNullException()
        {
            SeriesHeader sut = Build(new FakeSeriesDataService());

            _ = Assert.Throws<ArgumentNullException>(() => sut.Apply(Guid.Empty, null!));
        }

        [Fact]
        public void FavoriteGlyph_FollowsTheFavoriteFlag()
        {
            SeriesHeader sut = Build(new FakeSeriesDataService());
            string unmarked = sut.FavoriteGlyph;

            sut.Apply(Guid.Empty, new SeriesDetailData(
                new Series { Title = "TKKG", IsFavorite = true },
                Tiles: [], ProgressText: "", ProgressPercent: 0));

            Assert.True(sut.IsFavorite);
            Assert.NotEqual(unmarked, sut.FavoriteGlyph);
        }

        [Fact]
        public void ToggleFavorite_WithoutLoadedSeries_ChangesNothing()
        {
            SeriesHeader sut = Build(new FakeSeriesDataService());

            sut.ToggleFavoriteCommand.Execute(null);

            // Der Stern steht schon da, bevor eine Serie geladen ist; ein Klick darf dann
            // nicht irgendeinen Datensatz treffen.
            Assert.False(sut.IsFavorite);
        }

        [Fact]
        public async Task ToggleFavorite_WithLoadedSeries_StoresTheNewState()
        {
            FakeSeriesDataService seriesService = new();
            Series series = new() { Title = "TKKG" };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            SeriesHeader sut = Build(seriesService);
            sut.Apply(series.Id, new SeriesDetailData(series, Tiles: [], ProgressText: "", ProgressPercent: 0));

            sut.ToggleFavoriteCommand.Execute(null);

            Assert.True(sut.IsFavorite);
            Series? stored = await seriesService.GetByIdAsync(series.Id, TestContext.Current.CancellationToken);
            Assert.NotNull(stored);
            Assert.True(stored.IsFavorite);
        }

        private static SeriesHeader Build(FakeSeriesDataService seriesService)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<ICachedNewReleaseDataService>(_ => new FakeCachedNewReleaseDataService());
            ServiceProvider provider = services.BuildServiceProvider();

            return new SeriesHeader(
                provider.GetRequiredService<IServiceScopeFactory>(), CancellationToken.None);
        }
    }
}
