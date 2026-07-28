using EchoPlay.App.Models;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für Sortierung, Fortschrittsanzeige und das Markieren von Folgen auf der
    /// Serien-Detailseite. Laden und Wiedergabe deckt
    /// <see cref="SeriesDetailViewModelTests"/> ab.
    /// </summary>
    public sealed class SeriesDetailSortAndProgressTests
    {
        private static async Task<(SeriesDetailViewModel ViewModel, Guid SeriesId, FakeEpisodeDataService Episodes)>
            LoadAsync(FakeLocalizationService? localization = null, params (int? Number, string Title)[] episodes)
        {
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            Series series = new() { Title = "TKKG", IsSubscribed = true };
            await seriesService.AddAsync(series, TestContext.Current.CancellationToken);

            foreach ((int? number, string title) in episodes)
            {
                await episodeService.AddAsync(
                    new Episode { SeriesId = series.Id, EpisodeNumber = number, Title = title },
                    TestContext.Current.CancellationToken);
            }

            // Je Fake genau eine Instanz: Das ViewModel öffnet pro Aufruf einen eigenen
            // DI-Scope. Würde die Registrierung dort jeweils ein neues Objekt bauen, ginge
            // der geschriebene Zustand zwischen zwei Aufrufen verloren.
            FakePlaybackStateDataService playbackService = new();
            FakeLocalTrackDataService trackService = new();

            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);
            _ = services.AddScoped<IPlaybackStateDataService>(_ => playbackService);
            _ = services.AddScoped<ILocalTrackDataService>(_ => trackService);

            ServiceProvider provider = services.BuildServiceProvider();

            SeriesDetailViewModel viewModel = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakePlayerService(),
                new FakeClock(),
                localizationService: localization);

            await viewModel.LoadAsync(series.Id);

            return (viewModel, series.Id, episodeService);
        }

        [Fact]
        public async Task SortOrder_ByTitle_SortsAlphabetically()
        {
            (SeriesDetailViewModel viewModel, _, _) = await LoadAsync(
                null, (1, "Eins"), (2, "Zwei"), (3, "Drei"));

            viewModel.SortOrder = EpisodeSortOrder.Title;

            Assert.Equal(3, viewModel.Episodes[0].EpisodeNumber);
        }

        [Fact]
        public async Task SortOrder_NumberAscending_IsTheDefault()
        {
            (SeriesDetailViewModel viewModel, _, _) = await LoadAsync(
                null, (3, "Drei"), (1, "Eins"), (2, "Zwei"));

            Assert.Equal(1, viewModel.Episodes[0].EpisodeNumber);
        }

        [Fact]
        public async Task SortOrder_Change_NotifiesTheView()
        {
            (SeriesDetailViewModel viewModel, _, _) = await LoadAsync(null, (1, "Eins"));

            List<string> changed = [];
            viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

            viewModel.SortOrder = EpisodeSortOrder.Title;

            Assert.Contains(nameof(SeriesDetailViewModel.Episodes), changed);
        }

        [Fact]
        public async Task ProgressText_WithoutEpisodes_IsEmpty()
        {
            (SeriesDetailViewModel viewModel, _, _) = await LoadAsync();

            Assert.Equal(string.Empty, viewModel.ProgressText);
        }

        [Fact]
        public async Task ProgressText_UsesLocalizedPattern()
        {
            // Der Text steht in den Ressourcen – auf englischer Oberfläche darf hier
            // nichts Deutsches stehen bleiben.
            FakeLocalizationService localization = new(new Dictionary<string, string>
            {
                ["SeriesProgressTextPlural"] = "{0} of {1} episodes played"
            });

            (SeriesDetailViewModel viewModel, _, _) = await LoadAsync(
                localization, (1, "Eins"), (2, "Zwei"));

            Assert.Equal("0 of 2 episodes played", viewModel.ProgressText);
        }

        [Fact]
        public async Task ProgressText_WithSingleEpisode_UsesSingularResource()
        {
            // Eine Serie mit genau einer Folge ist der Grund für die zweite Ressource:
            // „0 von 1 Folgen gehört" wäre falsch gebeugt.
            FakeLocalizationService localization = new(new Dictionary<string, string>
            {
                ["SeriesProgressTextSingular"] = "{0} of {1} episode played"
            });

            (SeriesDetailViewModel viewModel, _, _) = await LoadAsync(localization, (1, "Eins"));

            Assert.Equal("0 of 1 episode played", viewModel.ProgressText);
        }

        [Fact]
        public async Task ProgressText_WithoutLocalization_FallsBackToGerman()
        {
            (SeriesDetailViewModel viewModel, _, _) = await LoadAsync(null, (1, "Eins"));

            Assert.Contains("von 1", viewModel.ProgressText, StringComparison.Ordinal);
        }

        [Fact]
        public async Task MarkAsPlayedAsync_SetsProgressToComplete()
        {
            (SeriesDetailViewModel viewModel, _, FakeEpisodeDataService episodes) =
                await LoadAsync(null, (1, "Eins"), (2, "Zwei"));

            Guid episodeId = episodes.All[0].Id;

            await viewModel.MarkAsPlayedAsync(episodeId);

            EpisodeTileViewModel tile = viewModel.Episodes.First(e => e.EpisodeId == episodeId);
            Assert.Equal(PlaybackStatus.Finished, tile.Progress);
        }

        [Fact]
        public async Task MarkAsUnplayedAsync_ResetsProgress()
        {
            (SeriesDetailViewModel viewModel, _, FakeEpisodeDataService episodes) =
                await LoadAsync(null, (1, "Eins"));

            Guid episodeId = episodes.All[0].Id;
            await viewModel.MarkAsPlayedAsync(episodeId);

            await viewModel.MarkAsUnplayedAsync(episodeId);

            EpisodeTileViewModel tile = viewModel.Episodes.First(e => e.EpisodeId == episodeId);
            Assert.Equal(PlaybackStatus.NotStarted, tile.Progress);
        }

        [Fact]
        public async Task MarkAsPlayedAsync_UpdatesProgressText()
        {
            (SeriesDetailViewModel viewModel, _, FakeEpisodeDataService episodes) =
                await LoadAsync(null, (1, "Eins"), (2, "Zwei"));

            await viewModel.MarkAsPlayedAsync(episodes.All[0].Id);

            Assert.Contains("1 von 2", viewModel.ProgressText, StringComparison.Ordinal);
        }

        [Fact]
        public async Task MarkAsPlayedAsync_UnknownEpisode_ChangesNothing()
        {
            (SeriesDetailViewModel viewModel, _, _) = await LoadAsync(null, (1, "Eins"));

            await viewModel.MarkAsPlayedAsync(Helpers.TestIds.EpisodeE);

            Assert.Equal(PlaybackStatus.NotStarted, viewModel.Episodes[0].Progress);
        }

        [Fact]
        public async Task HasSpecialEpisodes_WhenEpisodeHasNoNumber()
        {
            (SeriesDetailViewModel viewModel, _, _) = await LoadAsync(
                null, (1, "Reguläre Folge"), (null, "Sonderfolge"));

            Assert.True(viewModel.HasSpecialEpisodes);
            Assert.Equal(1, viewModel.SpecialEpisodeCount);
        }

        [Fact]
        public async Task Cleanup_LeavesTheViewModelUsable()
        {
            // Cleanup läuft beim Verlassen der Seite und darf keine Ausnahme werfen,
            // auch wenn nichts nachzuladen ist.
            (SeriesDetailViewModel viewModel, _, _) = await LoadAsync(null, (1, "Eins"));

            viewModel.Cleanup();
            viewModel.CancelPendingPriorityLoad();
        }
    }
}
