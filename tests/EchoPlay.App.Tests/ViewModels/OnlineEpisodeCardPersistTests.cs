using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft das Speichern des Gehört-Status, nachdem eine Folge beim Anbieter geöffnet wurde.
    /// </summary>
    /// <remarks>
    /// Das Öffnen selbst startet den Browser und ist deshalb nicht Teil dieser Tests; geprüft
    /// wird der Schritt danach. Scheitert er, darf die Kachel nicht reißen, aber der Fehler
    /// muss im Protokoll stehen, sonst fiele der verlorene Stand erst beim nächsten Start auf.
    /// </remarks>
    public sealed class OnlineEpisodeCardPersistTests
    {
        [Fact]
        public async Task PersistCompletedStatus_WritesTheHeardMark()
        {
            FakePlaybackStateDataService states = new();
            ServiceCollection services = new();
            _ = services.AddScoped<IPlaybackStateDataService>(_ => states);
            ServiceProvider provider = services.BuildServiceProvider();
            OnlineEpisodeCardViewModel card = new(
                new OnlineEpisodeCardData
                {
                    EpisodeId = TestIds.EpisodeA,
                    EpisodeNumber = 1,
                    Title = "Der Superhund",
                },
                scopeFactory: provider.GetRequiredService<IServiceScopeFactory>());

            await card.PersistCompletedStatusAsync();

            PlaybackState? stored = await states.GetByEpisodeIdAsync(TestIds.EpisodeA, TestContext.Current.CancellationToken);
            Assert.True(stored?.IsCompleted);
        }

        [Fact]
        public async Task PersistCompletedStatus_WhenStoringFails_LogsAWarning()
        {
            CapturingLoggerFactory loggers = new(new CapturingLogger());
            ServiceCollection services = new();
            _ = services.AddScoped<IPlaybackStateDataService>(_ => throw new InvalidOperationException("Datenbank gesperrt"));
            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(loggers);
            ServiceProvider provider = services.BuildServiceProvider();
            OnlineEpisodeCardViewModel card = new(
                new OnlineEpisodeCardData
                {
                    EpisodeId = TestIds.EpisodeA,
                    EpisodeNumber = 1,
                    Title = "Der Superhund",
                },
                scopeFactory: provider.GetRequiredService<IServiceScopeFactory>());

            await card.PersistCompletedStatusAsync();

            Assert.Contains(loggers.Logger.Entries, e => e.Level == "Warning" && e.Message.Contains("Datenbank gesperrt", StringComparison.Ordinal));
        }

        [Fact]
        public async Task PersistCompletedStatus_WithoutScopeFactory_DoesNothing()
        {
            OnlineEpisodeCardViewModel card = new(
                new OnlineEpisodeCardData
                {
                    EpisodeId = TestIds.EpisodeA,
                    EpisodeNumber = 1,
                    Title = "Der Superhund",
                });

            Exception? error = await Record.ExceptionAsync(card.PersistCompletedStatusAsync);

            Assert.Null(error);
        }
    }
}
