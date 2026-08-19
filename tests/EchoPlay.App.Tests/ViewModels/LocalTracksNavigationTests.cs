using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System.Collections.Generic;
using Xunit;

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

            // Eine reine Online-Serie hat keinen Ordner — der Tag-Manager hätte nichts
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

        private static LocalArtistCardViewModel BuildArtistCard(string? localFolderPath)
        {
            ServiceCollection services = new();
            ServiceProvider provider = services.BuildServiceProvider();

            return new LocalArtistCardViewModel(
                TestIds.SeriesA,
                "Die drei Fragezeichen",
                coverImage: null,
                localFolderPath,
                localEpisodeCount: 1,
                totalEpisodeCount: 1,
                isFavorite: false,
                isWatched: false,
                provider.GetRequiredService<IServiceScopeFactory>());
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
