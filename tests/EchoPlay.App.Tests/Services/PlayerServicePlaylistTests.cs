using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Media.Core;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft den Umgang des echten <see cref="PlayerService"/> mit seiner Abspielliste: Quellen
    /// einer abgelösten Folge werden geschlossen, und ein nachlaufender Stopp lässt eine
    /// inzwischen gestartete Folge in Ruhe.
    /// </summary>
    /// <remarks>
    /// <c>MediaPlayer</c> und <c>MediaSource</c> sind Betriebssystem-Typen und laufen im Testhost.
    /// Die Dateien sind leer: Geprüft wird die Verwaltung der Liste, nicht das Abspielen.
    /// </remarks>
    public sealed class PlayerServicePlaylistTests : IDisposable
    {
        private readonly string _directory;
        private readonly ServiceProvider _services;

        /// <summary>
        /// Legt je Test ein eigenes Verzeichnis und einen Container mit gefälschter Datenhaltung an.
        /// </summary>
        public PlayerServicePlaylistTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "EchoPlayPlayerTests_" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(_directory);

            ServiceCollection collection = new();
            _ = collection.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _services = collection.BuildServiceProvider();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _services.Dispose();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Fact]
        public async Task Play_ReplacingTheEpisode_ClosesTheSourcesOfThePreviousOne()
        {
            await using PlayerService player = CreatePlayer();

            player.Play(Guid.NewGuid(), CreateTracks("a1.mp3", "a2.mp3"));
            IReadOnlyList<MediaSource> firstSources = player.PlaylistSources;

            player.Play(Guid.NewGuid(), CreateTracks("b1.mp3"));

            Assert.Equal(2, firstSources.Count);
            Assert.All(firstSources, source => Assert.Equal(MediaSourceState.Closed, source.State));
            _ = Assert.Single(player.PlaylistSources);
            Assert.Equal(1, player.PlaylistItemCount);
        }

        [Fact]
        public async Task Stop_OvertakenByTheNextPlay_LeavesTheNewPlaylistAlone()
        {
            await using PlayerService player = CreatePlayer();
            player.Play(Guid.NewGuid(), CreateTracks("a1.mp3"));

            player.Stop();
            player.Play(Guid.NewGuid(), CreateTracks("b1.mp3", "b2.mp3"));
            await player.PipelineStopTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.Equal(2, player.PlaylistItemCount);
            Assert.Equal(2, player.PlaylistSources.Count);
        }

        [Fact]
        public async Task Stop_WithoutFollowingPlay_EmptiesTheListAndClosesItsSources()
        {
            await using PlayerService player = CreatePlayer();
            player.Play(Guid.NewGuid(), CreateTracks("a1.mp3", "a2.mp3"));
            IReadOnlyList<MediaSource> sources = player.PlaylistSources;

            player.Stop();
            await player.PipelineStopTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.Equal(0, player.PlaylistItemCount);
            Assert.Empty(player.PlaylistSources);
            Assert.All(sources, source => Assert.Equal(MediaSourceState.Closed, source.State));
        }

        private PlayerService CreatePlayer()
        {
            return new PlayerService(
                _services.GetRequiredService<IServiceScopeFactory>(),
                new FakeLoggerFactory(),
                new FakeClock());
        }

        private List<string> CreateTracks(params string[] fileNames)
        {
            List<string> paths = [];
            foreach (string fileName in fileNames)
            {
                string path = Path.Combine(_directory, fileName);
                File.WriteAllBytes(path, []);
                paths.Add(path);
            }

            return paths;
        }
    }
}
