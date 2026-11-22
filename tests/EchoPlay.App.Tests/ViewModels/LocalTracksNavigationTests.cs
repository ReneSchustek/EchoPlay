using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Sprünge aus der Spurenspalte in den Tag-Manager und den Zustand der
    /// Wiedergabe-Schaltfläche.
    /// </summary>
    /// <remarks>
    /// Beide Sprünge zeigen auf verschiedene Ordner: einer auf die ganze Serie, einer auf
    /// die gewählte Folge. Verwechselt man sie, öffnet der Tag-Manager hunderte Dateien
    /// statt der acht, die der Anwender meinte.
    /// </remarks>
    public sealed class LocalTracksNavigationTests
    {
        private const string SeriesFolder = @"D:\Media\TKKG";

        [Fact]
        public void OpenAllSeriesTracks_NavigatesToTheSeriesFolder()
        {
            List<string> requested = [];
            LocalTracksViewModel sut = Build(requested.Add);
            sut.SelectedArtist = BuildArtistCard(SeriesFolder);

            sut.OpenAllSeriesTracksCommand.Execute(null);

            Assert.Equal([SeriesFolder], requested);
        }

        [Fact]
        public void OpenAllSeriesTracks_WithoutSelectedSeries_StaysQuiet()
        {
            List<string> requested = [];
            LocalTracksViewModel sut = Build(requested.Add);

            sut.OpenAllSeriesTracksCommand.Execute(null);

            Assert.Empty(requested);
        }

        [Fact]
        public void OpenAllSeriesTracks_WithoutFolder_StaysQuiet()
        {
            List<string> requested = [];
            LocalTracksViewModel sut = Build(requested.Add);
            sut.SelectedArtist = BuildArtistCard(localFolderPath: null);

            // Eine reine Online-Serie hat keinen Ordner, der Tag-Manager hätte nichts
            // zu öffnen.
            sut.OpenAllSeriesTracksCommand.Execute(null);

            Assert.Empty(requested);
        }

        [Fact]
        public void OpenAllEpisodeTracks_WithoutSelectedEpisode_StaysQuiet()
        {
            List<string> requested = [];
            LocalTracksViewModel sut = Build(requested.Add);

            sut.OpenAllEpisodeTracksCommand.Execute(null);

            Assert.Empty(requested);
        }

        [Fact]
        public void NewList_HidesTheTrackPanelAndTheEmptyHint()
        {
            LocalTracksViewModel sut = Build(_ => { });

            Assert.Equal(Visibility.Collapsed, sut.TracksAccordionVisibility);
            Assert.Equal(Visibility.Visible, sut.TracksEmptyVisibility);
            Assert.Equal(string.Empty, sut.SelectedEpisodeTitle);
            Assert.False(sut.PlayEpisodeCommand.CanExecute(null));
        }

        [Fact]
        public void PlayEpisode_WithoutSelection_DoesNotStartAnything()
        {
            FakePlayerService player = new();
            LocalTracksViewModel sut = Build(_ => { }, player);

            sut.PlayEpisodeCommand.Execute(null);

            Assert.Empty(player.PlayCalls);
        }

        [Fact]
        public void Clear_EmptiesTheTrackList()
        {
            LocalTracksViewModel sut = Build(_ => { });

            sut.Clear();

            Assert.Empty(sut.Tracks);
            Assert.Equal(Visibility.Visible, sut.TracksEmptyVisibility);
        }

        [Fact]
        public async Task SetTracks_WithEpisodeNumber_NamesFolgeTracksAndTotalDuration()
        {
            LocalTracksViewModel sut = Build(_ => { });

            await sut.SetTracksAsync(
                BuildEpisodeCard(episodeNumber: 7),
                [Track(1, TimeSpan.FromMinutes(40)), Track(2, TimeSpan.FromMinutes(22).Add(TimeSpan.FromSeconds(5)))],
                TestContext.Current.CancellationToken);

            Assert.Equal("Folge 7 \u00B7 2 Tracks \u00B7 1:02:05", sut.TrackPanelSubtitle);
        }

        [Fact]
        public async Task SetTracks_WithoutEpisodeNumber_LeavesTheNumberOut()
        {
            LocalTracksViewModel sut = Build(_ => { });

            await sut.SetTracksAsync(
                BuildEpisodeCard(episodeNumber: null),
                [Track(1, TimeSpan.FromSeconds(95))],
                TestContext.Current.CancellationToken);

            // Eine Sonderfolge ohne Nummer bekäme sonst ein „Folge  ·" ohne Zahl.
            Assert.Equal("1 Track \u00B7 1:35", sut.TrackPanelSubtitle);
        }

        private static LocalEpisodeCardViewModel BuildEpisodeCard(int? episodeNumber)
            => new(
                new LocalEpisodeCardData
                {
                    EpisodeId = Guid.NewGuid(),
                    EpisodeNumber = episodeNumber,
                    Title = "Der Schatz im Bergsee",
                    LocalTrackCount = 1,
                });

        private static LocalTrack Track(int number, TimeSpan duration)
            => new() { TrackNumber = number, FilePath = $@"D:\Media\TKKG\{number:00}.mp3", Duration = duration };

        private static LocalArtistCardViewModel BuildArtistCard(string? localFolderPath)
        {
            ServiceCollection services = new();
            ServiceProvider provider = services.BuildServiceProvider();

            return new LocalArtistCardViewModel(
                new LocalArtistCardData
                {
                    SeriesId = TestIds.SeriesA,
                    Title = "Die drei Fragezeichen",
                    LocalFolderPath = localFolderPath,
                    LocalEpisodeCount = 1,
                    TotalEpisodeCount = 1,
                },
                coverImage: null,
                scopeFactory: provider.GetRequiredService<IServiceScopeFactory>());
        }

        private static LocalTracksViewModel Build(
            System.Action<string> onNavigate, FakePlayerService? player = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ITrackTitleResolver>(_ => new FakeTrackTitleResolver());
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            ServiceProvider provider = services.BuildServiceProvider();

            return new LocalTracksViewModel(
                player ?? new FakePlayerService(),
                provider.GetRequiredService<IServiceScopeFactory>(),
                onNavigate);
        }
    }
}
