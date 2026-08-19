using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft das Sichern des Hörstands — die Stelle, an der zuletzt aufgehört wurde.
    /// </summary>
    /// <remarks>
    /// Geht das schief, merkt es der Anwender sofort: Die Folge beginnt wieder von vorn. Zwei
    /// Zusagen tragen die Klasse. Es gibt nie zwei Schreibvorgänge gleichzeitig, und ein
    /// gescheiterter Schreibvorgang stört die Wiedergabe nicht.
    /// </remarks>
    public sealed class PlaybackStateWriterTests
    {
        private static readonly Guid EpisodeId = new("aaaaaaaa-1111-2222-3333-444444444444");
        private static readonly DateTime Now = new(2026, 8, 17, 20, 30, 0, DateTimeKind.Utc);
        private static readonly TimeSpan Position = TimeSpan.FromMinutes(12.5);

        [Fact]
        public async Task Save_WithoutEpisodeId_WritesNothing()
        {
            ControllablePlaybackStateDataService data = new();
            using PlaybackStateWriter writer = BuildWriter(data);

            await writer.SaveAsync(Guid.Empty, Position);

            // Beim Abspielen eines Ordners gibt es keine Folge, zu der ein Stand gehören
            // könnte. Dann wird nicht einmal gelesen.
            Assert.Equal(0, data.ReadCallCount);
            Assert.Equal(0, data.AddCallCount);
        }

        [Fact]
        public async Task Save_ForEpisodeWithoutState_CreatesOneWithPositionAndTime()
        {
            ControllablePlaybackStateDataService data = new();
            using PlaybackStateWriter writer = BuildWriter(data);

            await writer.SaveAsync(EpisodeId, Position);

            Assert.Equal(1, data.AddCallCount);
            Assert.Equal(0, data.UpdateCallCount);

            PlaybackState stored = Assert.Single(await data.GetAllAsync(TestContext.Current.CancellationToken));
            Assert.Equal(EpisodeId, stored.EpisodeId);
            Assert.Equal(Position, stored.LastPosition);
            Assert.Equal(Now, stored.LastPlayedAt);
        }

        [Fact]
        public async Task Save_ForEpisodeWithState_UpdatesInsteadOfCreatingASecondOne()
        {
            ControllablePlaybackStateDataService data = new();
            data.Seed(new PlaybackState
            {
                EpisodeId = EpisodeId,
                LastPosition = TimeSpan.FromMinutes(3),
                LastPlayedAt = Now.AddDays(-1),
            });

            using PlaybackStateWriter writer = BuildWriter(data);

            await writer.SaveAsync(EpisodeId, Position);

            // Ein zweiter Datensatz für dieselbe Folge wäre ein doppelter Hörstand — welcher
            // dann beim nächsten Öffnen gewinnt, wäre Zufall.
            Assert.Equal(0, data.AddCallCount);
            Assert.Equal(1, data.UpdateCallCount);

            PlaybackState stored = Assert.Single(await data.GetAllAsync(TestContext.Current.CancellationToken));
            Assert.Equal(Position, stored.LastPosition);
            Assert.Equal(Now, stored.LastPlayedAt);
        }

        [Fact]
        public async Task Save_WhenWritingFails_DoesNotThrow()
        {
            ControllablePlaybackStateDataService data = new()
            {
                FailWriteWith = "Datenbank gesperrt",
            };

            using PlaybackStateWriter writer = BuildWriter(data);

            // Der Aufrufer ist die laufende Wiedergabe. Fliegt hier ein Fehler heraus, bricht
            // sie ab — und das wäre schlimmer als ein verlorener Hörstand.
            await writer.SaveAsync(EpisodeId, Position);

            Assert.Equal(0, data.AddCallCount);
        }

        [Fact]
        public async Task Save_WhenAlreadyWriting_SkipsTheSecondAttempt()
        {
            ControllablePlaybackStateDataService data = new();
            using PlaybackStateWriter writer = BuildWriter(data);

            data.BlockRead();
            Task pending = writer.SaveAsync(EpisodeId, Position);

            // Der zweite Aufruf trifft auf die belegte Sperre. Er darf nicht warten, sondern
            // fällt aus — der nächste Durchlauf bringt ohnehin einen aktuelleren Stand.
            await writer.SaveAsync(EpisodeId, TimeSpan.FromMinutes(20));

            Assert.Equal(1, data.ReadCallCount);

            data.ReleaseRead();
            await pending;

            Assert.Equal(1, data.AddCallCount);
        }

        [Fact]
        public async Task Save_AfterAPreviousSaveFinished_WritesAgain()
        {
            ControllablePlaybackStateDataService data = new();
            using PlaybackStateWriter writer = BuildWriter(data);

            await writer.SaveAsync(EpisodeId, Position);
            await writer.SaveAsync(EpisodeId, TimeSpan.FromMinutes(20));

            // Die Sperre darf nur den gleichzeitigen Zugriff verhindern, nicht den nächsten
            // regulären — sonst stünde der Hörstand nach dem ersten Schreiben still.
            Assert.Equal(1, data.AddCallCount);
            Assert.Equal(1, data.UpdateCallCount);
        }

        [Fact]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            PlaybackStateWriter writer = BuildWriter(new ControllablePlaybackStateDataService());

            writer.Dispose();
            writer.Dispose();
        }

        private static PlaybackStateWriter BuildWriter(ControllablePlaybackStateDataService data)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IPlaybackStateDataService>(_ => data);

            ServiceProvider provider = services.BuildServiceProvider();

            return new PlaybackStateWriter(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeLogger(),
                new FakeClock { UtcNow = Now });
        }
    }
}
