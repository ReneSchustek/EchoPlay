using EchoPlay.App.Models;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xunit;

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
    /// Der Fall „bereits erschienen, aber nicht lokal vorhanden" bleibt bewusst außen vor:
    /// Er baut eine Suchadresse und öffnet sie im Browser — ein Test darf keine fremde
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

        private static NewEpisodeCardViewModel Build(
            FakeErrorDialogService errorDialog,
            FakeClock clock,
            DateTime? releaseDate,
            bool isAnnounced)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService());

            ServiceProvider provider = services.BuildServiceProvider();

            return new NewEpisodeCardViewModel(
                TestIds.EpisodeA,
                TestIds.SeriesA,
                "TKKG",
                "Folge 250",
                coverImage: null,
                PlaybackStatus.NotStarted,
                progressPercent: 0,
                hasLocalTrack: false,
                isAnnounced,
                provider.GetRequiredService<IServiceScopeFactory>(),
                errorDialog,
                new FakeConfirmationDialogService(),
                new FakePlayerService(),
                episodeNumber: 250,
                releaseDate: releaseDate,
                clock: clock);
        }
    }
}
