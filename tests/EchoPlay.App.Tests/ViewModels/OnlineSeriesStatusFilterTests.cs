using EchoPlay.App.Models;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft den Statusfilter der Online-Mediathek: welche Serien er für „neu“, „am Hören“
    /// und „durchgehört“ übrig lässt.
    /// </summary>
    public sealed class OnlineSeriesStatusFilterTests
    {
        [Theory]
        [InlineData(SeriesStatusFilter.New, "Neu")]
        [InlineData(SeriesStatusFilter.InProgress, "Angefangen")]
        [InlineData(SeriesStatusFilter.Finished, "Fertig")]
        public void StatusFilter_LeavesOnlyTheMatchingSeries(SeriesStatusFilter filter, string expected)
        {
            OnlineSeriesViewModel sut = new();
            sut.SetAllSeries(
            [
                Card("Neu", newCount: 2, inProgress: 0, finished: 0),
                Card("Angefangen", newCount: 0, inProgress: 1, finished: 1),
                Card("Fertig", newCount: 0, inProgress: 0, finished: 3),
            ]);

            sut.StatusFilter = filter;

            Assert.Equal([expected], sut.Series.Select(s => s.Title));
        }

        [Fact]
        public void StatusFilter_All_ShowsEverySeries()
        {
            OnlineSeriesViewModel sut = new();
            sut.SetAllSeries([Card("Neu", 2, 0, 0), Card("Fertig", 0, 0, 3)]);
            sut.StatusFilter = SeriesStatusFilter.New;

            sut.StatusFilter = SeriesStatusFilter.All;

            Assert.Equal(2, sut.Series.Count);
        }

        private static SeriesCardViewModel Card(string title, int newCount, int inProgress, int finished)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            ServiceProvider provider = services.BuildServiceProvider();

            return new SeriesCardViewModel(
                Guid.NewGuid(),
                title,
                coverImage: null,
                totalEpisodeCount: 3,
                newEpisodeCount: newCount,
                inProgressCount: inProgress,
                finishedCount: finished,
                isSubscribed: true,
                isFavorite: false,
                isWatched: false,
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeConfirmationDialogService(),
                new FakeLocalizationService());
        }
    }
}
