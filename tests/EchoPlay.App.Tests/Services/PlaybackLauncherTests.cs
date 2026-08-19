using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft den Start einer Wiedergabe: Spuren laden, die Fortsetzstelle bestimmen und an
    /// das Abspielgerät übergeben.
    /// </summary>
    /// <remarks>
    /// Geprüft wird ausschließlich der lokale Weg und die Auswahl der Anbieter-Ziele. Die
    /// Zweige, die Spotify oder Apple Music tatsächlich öffnen, bleiben außen vor: Sie starten
    /// fremde Anwendungen, und das hat in einem Testlauf nichts verloren. Genau dafür ist die
    /// Zielermittlung im Produktivcode vom Öffnen getrennt.
    /// </remarks>
    public sealed class PlaybackLauncherTests
    {
        private static readonly Guid EpisodeId = new("bbbbbbbb-1111-2222-3333-555555555555");

        [Fact]
        public async Task PlayEpisode_WithoutTracks_StartsNothing()
        {
            FakePlayerService player = new();
            IServiceScopeFactory scopeFactory = BuildScopeFactory();

            await PlaybackLauncher.PlayEpisodeAsync(
                scopeFactory, player, EpisodeId, TestContext.Current.CancellationToken);

            // Eine Folge ohne Dateien kann nicht abgespielt werden. Ein Aufruf ins Leere
            // würde das Abspielgerät mit einer leeren Liste in einen unklaren Zustand bringen.
            Assert.Empty(player.PlayCalls);
        }

        [Fact]
        public async Task PlayEpisode_WithTracks_PassesPathsAndDurations()
        {
            FakePlayerService player = new();
            IServiceScopeFactory scopeFactory = BuildScopeFactory(TwoTracks());

            await PlaybackLauncher.PlayEpisodeAsync(
                scopeFactory, player, EpisodeId, TestContext.Current.CancellationToken);

            (Guid episodeId, IReadOnlyList<string> paths, int startIndex, TimeSpan _, IReadOnlyList<TimeSpan>? durations) =
                Assert.Single(player.PlayCalls);

            Assert.Equal(EpisodeId, episodeId);
            Assert.Equal(["D:/Media/01.mp3", "D:/Media/02.mp3"], paths);
            Assert.Equal(0, startIndex);

            // Die Dauern gehen mit: Die gespeicherte Stelle gilt für die ganze Folge, und
            // ohne sie kann das Abspielgerät nicht bestimmen, in welcher Datei sie liegt.
            Assert.NotNull(durations);
            Assert.Equal(2, durations.Count);
        }

        [Fact]
        public async Task PlayEpisode_WithoutSavedState_StartsFromTheBeginning()
        {
            FakePlayerService player = new();
            IServiceScopeFactory scopeFactory = BuildScopeFactory(TwoTracks());

            await PlaybackLauncher.PlayEpisodeAsync(
                scopeFactory, player, EpisodeId, TestContext.Current.CancellationToken);

            Assert.Equal(TimeSpan.Zero, Assert.Single(player.PlayCalls).Item4);
        }

        [Fact]
        public async Task PlayEpisode_WithOpenPosition_ResumesThere()
        {
            FakePlayerService player = new();
            ControllablePlaybackStateDataService states = new();
            states.Seed(new PlaybackState
            {
                EpisodeId = EpisodeId,
                LastPosition = TimeSpan.FromMinutes(20),
            });

            IServiceScopeFactory scopeFactory = BuildScopeFactory(TwoTracks(), states);

            await PlaybackLauncher.PlayEpisodeAsync(
                scopeFactory, player, EpisodeId, TestContext.Current.CancellationToken);

            Assert.Equal(TimeSpan.FromMinutes(20), Assert.Single(player.PlayCalls).Item4);
        }

        [Fact]
        public async Task PlayEpisode_WhenPositionIsAtTheEnd_StartsOver()
        {
            FakePlayerService player = new();
            ControllablePlaybackStateDataService states = new();

            // Zwei Spuren zu je 30 Minuten — die gespeicherte Stelle liegt am Ende.
            states.Seed(new PlaybackState
            {
                EpisodeId = EpisodeId,
                LastPosition = TimeSpan.FromMinutes(60),
            });

            IServiceScopeFactory scopeFactory = BuildScopeFactory(TwoTracks(), states);

            await PlaybackLauncher.PlayEpisodeAsync(
                scopeFactory, player, EpisodeId, TestContext.Current.CancellationToken);

            // Eine durchgehörte Folge steht mit ihrer Stelle am Ende. Dort fortzusetzen
            // hieße, sofort wieder aufzuhören.
            Assert.Equal(TimeSpan.Zero, Assert.Single(player.PlayCalls).Item4);
        }

        [Fact]
        public async Task PlayEpisode_WithoutScopeFactory_Throws()
        {
            FakePlayerService player = new();

            _ = await Assert.ThrowsAsync<ArgumentNullException>(
                () => PlaybackLauncher.PlayEpisodeAsync(null!, player, EpisodeId, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task PlayOrOpenProvider_WithLocalFiles_PlaysInsteadOfOpeningTheProvider()
        {
            FakePlayerService player = new();
            IServiceScopeFactory scopeFactory = BuildScopeFactory(TwoTracks());

            EpisodeLaunchResult result = await PlaybackLauncher.PlayOrOpenProviderAsync(
                scopeFactory,
                player,
                EpisodeId,
                "Die drei Fragezeichen",
                "Folge 001",
                cancellationToken: TestContext.Current.CancellationToken);

            // „Erst lokal, sonst online": Liegt die Datei vor, verlässt niemand die Anwendung.
            Assert.Equal(EpisodeLaunchResult.PlayedLocally, result);
            _ = Assert.Single(player.PlayCalls);
        }

        [Fact]
        public void BuildSpotifyTargets_WithAlbumId_PrefersTheAlbumOverASearch()
        {
            bool found = PlaybackLauncher.TryBuildSpotifyTargets(
                "4aawyAB9vmqN3uQ7FjRGTy", "Die drei Fragezeichen", "Folge 001",
                out string? appUri, out string? webUrl);

            Assert.True(found);
            Assert.Contains("4aawyAB9vmqN3uQ7FjRGTy", appUri, StringComparison.Ordinal);
            Assert.Contains("4aawyAB9vmqN3uQ7FjRGTy", webUrl, StringComparison.Ordinal);
        }

        [Fact]
        public void BuildSpotifyTargets_WithoutAlbumId_FallsBackToASearch()
        {
            bool found = PlaybackLauncher.TryBuildSpotifyTargets(
                null, "Die drei Fragezeichen", "Folge 001",
                out string? appUri, out string? webUrl);

            // Ohne Kennung bleibt die Suche — besser als gar kein Ziel, denn der Anwender
            // landet immerhin bei seiner Serie.
            Assert.True(found);
            Assert.NotNull(appUri);
            Assert.NotNull(webUrl);
        }

        private static Dictionary<Guid, IReadOnlyList<LocalTrack>> TwoTracks()
        {
            return new Dictionary<Guid, IReadOnlyList<LocalTrack>>
            {
                [EpisodeId] =
                [
                    new LocalTrack { FilePath = "D:/Media/01.mp3", Duration = TimeSpan.FromMinutes(30) },
                    new LocalTrack { FilePath = "D:/Media/02.mp3", Duration = TimeSpan.FromMinutes(30) },
                ],
            };
        }

        private static IServiceScopeFactory BuildScopeFactory(
            Dictionary<Guid, IReadOnlyList<LocalTrack>>? tracks = null,
            ControllablePlaybackStateDataService? states = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService(tracks));
            _ = services.AddScoped<IPlaybackStateDataService>(_ => states ?? new ControllablePlaybackStateDataService());
            _ = services.AddScoped<ILoggerFactory>(_ => new FakeLoggerFactory());

            return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        }
    }
}
