using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft die Bedienung des echten <see cref="PlayerService"/>: Spuren und Gesamtdauer der
    /// laufenden Folge, Einschlaf-Timer, Springen und den Fehlerweg beim Start.
    /// </summary>
    /// <remarks>
    /// <c>MediaPlayer</c> ist ein Betriebssystem-Typ und läuft im Testhost. Die Dateien sind leer;
    /// geprüft wird der Zustand des Dienstes, nicht das Abspielen.
    /// </remarks>
    public sealed class PlayerServiceControlTests : IDisposable
    {
        private readonly string _directory;
        private readonly ServiceProvider _services;

        /// <summary>
        /// Legt je Test ein eigenes Verzeichnis und einen Container mit gefälschter Datenhaltung an.
        /// </summary>
        public PlayerServiceControlTests()
        {
            _directory = Path.Combine(Path.GetTempPath(), "EchoPlayPlayerControlTests_" + Guid.NewGuid().ToString("N"));
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
        public async Task Play_WithDurations_KnowsTracksAndTotalDuration()
        {
            await using PlayerService player = CreatePlayer();
            List<string> tracks = CreateTracks("a1.mp3", "a2.mp3");

            player.Play(Guid.NewGuid(), tracks, trackDurations: [TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(4)]);

            Assert.Equal(tracks, player.CurrentTrackPaths);
            Assert.Equal(TimeSpan.FromMinutes(7), player.OverallDuration);
            Assert.True(player.OverallPosition >= TimeSpan.Zero);
        }

        [Fact]
        public async Task SetSleepTimer_ReportsRemainingTimeAndChange()
        {
            await using PlayerService player = CreatePlayer();

            // Nicht die Meldungen zählen: Der Dienst meldet auch von sich aus, über den
            // Sekundentakt oder die Wiedergabesitzung, und im Release-Lauf waren es dann zwei.
            // Belegt wird, dass eine Meldung den gesetzten Timer schon sieht.
            System.Collections.Concurrent.ConcurrentQueue<TimeSpan?> seen = new();
            player.StateChanged += (_, _) => seen.Enqueue(player.SleepTimerRemaining);

            player.SetSleepTimer(TimeSpan.FromMinutes(15));

            Assert.Equal(TimeSpan.FromMinutes(15), player.SleepTimerRemaining);
            Assert.Contains(TimeSpan.FromMinutes(15), seen);

            player.SetSleepTimer(null);
            Assert.Null(player.SleepTimerRemaining);
        }

        [Fact]
        public async Task SkipAndSeek_KeepThePlaylist()
        {
            // Die Dateien sind leer, die Pipeline kann sie nicht öffnen. Je nachdem, wie weit sie
            // damit ist, weist die Abspielliste den Sprung mit einem COM-Fehler ab; der Dienst
            // darf ihn nicht an die Bedienung weiterreichen.
            await using PlayerService player = CreatePlayer();
            player.Play(Guid.NewGuid(), CreateTracks("a1.mp3", "a2.mp3"));

            player.SkipToNext();
            player.SkipToPrevious();
            player.SeekTo(TimeSpan.Zero);

            Assert.Equal(2, player.PlaylistItemCount);
        }

        [Fact]
        public async Task Play_WithRelativePath_ReportsTheErrorAndForgetsTheEpisode()
        {
            await using PlayerService player = CreatePlayer();
            string? error = null;
            player.ErrorOccurred += (_, message) => error = message;

            // Eine relative Angabe ist keine gültige Adresse; die Abspielliste weist sie ab.
            player.Play(Guid.NewGuid(), ["relativ.mp3"]);

            Assert.NotNull(error);
            Assert.Empty(player.CurrentTrackPaths);
            Assert.Equal(TimeSpan.Zero, player.OverallDuration);
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
