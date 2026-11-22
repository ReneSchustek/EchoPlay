using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft den Klick auf eine Neuerscheinungs-Kachel, deren Folge noch gar nicht
    /// erschienen ist.
    /// </summary>
    /// <remarks>
    /// Maßgeblich ist das Erscheinungsdatum, nicht das Kennzeichen: Apple Music führt
    /// angekündigte Alben bereits als Vorabveröffentlichung. Ohne diese Weiche landete der
    /// Anwender beim Anbieter vor einem Album, das dort nichts abspielt.
    ///
    /// Der Fall „bereits erschienen, aber nicht lokal vorhanden" bleibt außen vor:
    /// Er baut eine Suchadresse und öffnet sie im Browser, und ein Test darf keine fremde
    /// Anwendung starten.
    /// </remarks>
    public sealed class NewEpisodeCardPlayTests
    {
        [Fact]
        public async Task Play_WithAReleaseDateInTheFuture_SaysItIsNotOutYet()
        {
            FakeClock clock = new();
            FakeErrorDialogService errorDialog = new();
            NewEpisodeCardViewModel card = Build(
                errorDialog, clock, releaseDate: clock.UtcNow.AddDays(14), isAnnounced: true);

            card.PlayCommand.Execute(null);
            (string Title, string Message) shown = await errorDialog.FirstDialogShown
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.Contains("verfügbar", shown.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Play_ForAnAnnouncementWithoutADate_SaysItIsNotOutYet()
        {
            FakeErrorDialogService errorDialog = new();
            NewEpisodeCardViewModel card = Build(
                errorDialog, new FakeClock(), releaseDate: null, isAnnounced: true);

            card.PlayCommand.Execute(null);
            (string Title, string Message) shown = await errorDialog.FirstDialogShown
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            // Ohne Datum bleibt nur das Kennzeichen als Anhalt.
            Assert.Contains("verfügbar", shown.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Play_WithoutFileOrProviderHint_SaysItIsNotPlayable()
        {
            // Erschienen, aber weder als Datei da noch beim Anbieter zu finden: Der Hinweis
            // unterscheidet das vom angekündigten Fall.
            FakeErrorDialogService errorDialog = new();
            NewEpisodeCardViewModel card = Build(
                errorDialog, new FakeClock(), releaseDate: null, isAnnounced: false, titles: (string.Empty, string.Empty));

            card.PlayCommand.Execute(null);
            _ = await errorDialog.FirstDialogShown
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.Equal(EchoPlay.Core.Models.DialogKey.EpisodeNotPlayable, Assert.Single(errorDialog.ShownKeys));
        }

        private static NewEpisodeCardViewModel Build(
            FakeErrorDialogService errorDialog,
            FakeClock clock,
            DateTime? releaseDate,
            bool isAnnounced,
            (string Series, string Episode)? titles = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService());
            _ = services.AddScoped<EchoPlay.Logger.Abstractions.ILoggerFactory>(_ => new FakeLoggerFactory());

            ServiceProvider provider = services.BuildServiceProvider();

            NewEpisodeCardData data = new()
            {
                EpisodeId = TestIds.EpisodeA,
                SeriesId = TestIds.SeriesA,
                SeriesName = titles?.Series ?? "TKKG",
                EpisodeTitle = titles?.Episode ?? "Folge 250",
                IsAnnounced = isAnnounced,
                EpisodeNumber = 250,
                ReleaseDate = releaseDate,
            };
            return new NewEpisodeCardViewModel(
                data,
                coverImage: null,
                provider.GetRequiredService<IServiceScopeFactory>(),
                new NewEpisodeCardServices(errorDialog, new FakeConfirmationDialogService(), new FakePlayerService(), null),
                clock);
        }
    }
}
