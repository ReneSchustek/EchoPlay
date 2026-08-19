using EchoPlay.App.Models;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für die Kachel einer Neuerscheinung: Beschriftung, Kennzeichen und die
    /// drei Aktionen (abspielen, als gehört, als ungehört).
    /// </summary>
    /// <remarks>
    /// Die Farbe des Kennzeichens kommt aus der Palette und bleibt ohne laufendes
    /// Fenster leer — geprüft werden Text und Sichtbarkeit, die davon unabhängig sind.
    /// </remarks>
    public sealed class NewEpisodeCardViewModelTests
    {
        private static readonly DateTime Heute = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

        private static (NewEpisodeCardViewModel Card, FakePlaybackStateDataService States, FakeErrorDialogService Errors)
            Build(
                string episodeTitle = "Folge 1",
                bool hasLocalTrack = true,
                bool isAnnounced = false,
                int? episodeNumber = null,
                DateTime? releaseDate = null,
                PlaybackStatus status = PlaybackStatus.NotStarted,
                double progressPercent = 0,
                bool confirmResult = true,
                string seriesName = "TKKG")
        {
            FakePlaybackStateDataService states = new();
            FakeErrorDialogService errors = new();

            ServiceCollection services = new();
            _ = services.AddScoped<IPlaybackStateDataService>(_ => states);
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService());

            ServiceProvider provider = services.BuildServiceProvider();

            NewEpisodeCardViewModel card = new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                seriesName,
                episodeTitle,
                coverImage: null,
                status,
                progressPercent,
                hasLocalTrack,
                isAnnounced,
                provider.GetRequiredService<IServiceScopeFactory>(),
                errors,
                new FakeConfirmationDialogService(confirmResult),
                new FakePlayerService(),
                episodeNumber,
                releaseDate,
                localizationService: null,
                clock: new FakeClock { UtcNow = Heute });

            return (card, states, errors);
        }

        /// <summary>
        /// Lässt einen abgelehnten Befehl auslaufen. Bei einer Ablehnung ändert sich
        /// nichts, worauf sich warten ließe — deshalb ein paar Durchläufe der
        /// Fortsetzungen statt einer Wartezeit.
        /// </summary>
        private static async Task SettleAsync()
        {
            for (int durchlauf = 0; durchlauf < 20; durchlauf++)
            {
                await Task.Yield();
            }
        }

        [Fact]
        public void Titel_VerliertSerienNamenUndFolgennummer()
        {
            // Der Albumname des Anbieters wiederholt Serie und Nummer. Auf der Kachel
            // stünde dann dreimal dasselbe.
            (NewEpisodeCardViewModel card, _, _) = Build(episodeTitle: "TKKG - Folge 26 - Zusammengewachsen");

            Assert.Equal("Zusammengewachsen", card.EpisodeTitle);
        }

        [Fact]
        public void Titel_BleibtStehenWennNichtsUebrigBliebe()
        {
            (NewEpisodeCardViewModel card, _, _) = Build(episodeTitle: "TKKG", seriesName: "TKKG");

            Assert.Equal("TKKG", card.EpisodeTitle);
        }

        [Fact]
        public void Folgennummer_ErscheintNurWennSieBekanntIst()
        {
            (NewEpisodeCardViewModel mitNummer, _, _) = Build(episodeNumber: 229);
            (NewEpisodeCardViewModel ohneNummer, _, _) = Build();

            Assert.Equal("Folge 229", mitNummer.EpisodeNumberText);
            Assert.Null(ohneNummer.EpisodeNumberText);
        }

        [Fact]
        public void Infozeile_NenntNummerDatumUndHerkunft()
        {
            (NewEpisodeCardViewModel card, _, _) = Build(
                hasLocalTrack: false,
                episodeNumber: 170,
                releaseDate: new DateTime(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal("Nr. 170 · 14.08.2026 · online", card.InfoLineText);
            Assert.Equal(Visibility.Visible, card.InfoLineVisibility);
            Assert.True(card.IsOnlineOnly);
        }

        [Fact]
        public void Infozeile_BleibtOhneErscheinungsdatumAus()
        {
            (NewEpisodeCardViewModel card, _, _) = Build(episodeNumber: 5);

            Assert.Null(card.InfoLineText);
            Assert.Equal(Visibility.Collapsed, card.InfoLineVisibility);
        }

        [Fact]
        public void Ankuendigung_ZeigtDatumUndKennzeichen()
        {
            (NewEpisodeCardViewModel card, _, _) = Build(
                hasLocalTrack: false,
                isAnnounced: true,
                episodeNumber: 26,
                releaseDate: Heute.AddDays(30));

            Assert.Equal("17.09.2026", card.ReleaseDateText);
            Assert.Equal(Visibility.Visible, card.ReleaseDateVisibility);
            Assert.Equal("Angekündigt", card.BadgeText);
            Assert.Equal(Visibility.Visible, card.BadgeVisibility);
            Assert.Contains("angekündigt", card.InfoLineText, StringComparison.Ordinal);
        }

        [Fact]
        public void FrischeFolge_TraegtDasNeuKennzeichen()
        {
            (NewEpisodeCardViewModel card, _, _) = Build(releaseDate: Heute.AddDays(-3));

            Assert.Equal("Neu", card.BadgeText);
            Assert.Equal(Visibility.Visible, card.BadgeVisibility);
            Assert.Null(card.ReleaseDateText);
        }

        [Fact]
        public void AeltereFolge_TraegtKeinKennzeichen()
        {
            (NewEpisodeCardViewModel card, _, _) = Build(releaseDate: Heute.AddDays(-30));

            Assert.Null(card.BadgeText);
            Assert.Equal(Visibility.Collapsed, card.BadgeVisibility);
            Assert.Null(card.BadgeBrush);
        }

        [Fact]
        public void Fortschrittsbalken_ErscheintNurBeiAngefangenenFolgen()
        {
            (NewEpisodeCardViewModel inProgress, _, _) = Build(status: PlaybackStatus.InProgress, progressPercent: 42);
            (NewEpisodeCardViewModel finished, _, _) = Build(status: PlaybackStatus.Finished);

            Assert.Equal(Visibility.Visible, inProgress.ProgressBarVisibility);
            Assert.Equal(Visibility.Collapsed, inProgress.CompletedCheckVisibility);
            Assert.Equal(42, inProgress.ProgressPercent);
            Assert.Equal(Visibility.Visible, finished.CompletedCheckVisibility);
            Assert.Equal(Visibility.Collapsed, finished.ProgressBarVisibility);
        }

        [Fact]
        public void CoverTausch_IgnoriertEinLeeresBild()
        {
            // Ein fehlgeschlagener Nachlade-Versuch darf das bereits gezeigte Serien-Cover
            // nicht durch eine Lücke ersetzen.
            (NewEpisodeCardViewModel card, _, _) = Build();

            card.UpdateCoverImage(null);

            Assert.False(card.HasEpisodeCover);
            Assert.Null(card.CoverImage);
        }

        [Fact]
        public void CoverFreigabe_SetztDasKennzeichenZurueck()
        {
            (NewEpisodeCardViewModel card, _, _) = Build();

            card.ClearCoverImage();

            Assert.False(card.HasEpisodeCover);
            Assert.Null(card.CoverImage);
        }

        [Fact]
        public void Automationsnamen_NennenDieFolge()
        {
            (NewEpisodeCardViewModel card, _, _) = Build(episodeTitle: "Der Superhund");

            Assert.Contains("Der Superhund", card.PlayAutomationName, StringComparison.Ordinal);
            Assert.Contains("Der Superhund", card.ActionsAutomationName, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Abspielen_MeldetEineNochNichtErschieneneFolge()
        {
            (NewEpisodeCardViewModel card, _, FakeErrorDialogService errors) = Build(
                hasLocalTrack: false,
                isAnnounced: true,
                releaseDate: Heute.AddDays(10));

            card.PlayCommand.Execute(null);
            _ = await errors.FirstDialogShown.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            (string Title, string Message) dialog = Assert.Single(errors.ShownDialogs);
            Assert.Equal("Noch nicht verfügbar", dialog.Title);
        }

        [Fact]
        public async Task AlsGehoert_SchreibtDenStandUndSetztDieAnzeige()
        {
            (NewEpisodeCardViewModel card, FakePlaybackStateDataService states, _) =
                Build(status: PlaybackStatus.InProgress, progressPercent: 30);

            card.MarkAsPlayedCommand.Execute(null);
            await ChangeSignals.WaitForAsync(card, () => card.Status == PlaybackStatus.Finished, "Folge gilt als gehört");

            IReadOnlyList<PlaybackState> stored = await states.GetAllAsync(TestContext.Current.CancellationToken);
            Assert.True(Assert.Single(stored).IsCompleted);
            Assert.Equal(PlaybackStatus.Finished, card.Status);
            Assert.Equal(100, card.ProgressPercent);
        }

        [Fact]
        public async Task AlsGehoert_BleibtNachAblehnungAus()
        {
            (NewEpisodeCardViewModel card, FakePlaybackStateDataService states, _) =
                Build(status: PlaybackStatus.InProgress, progressPercent: 30, confirmResult: false);

            card.MarkAsPlayedCommand.Execute(null);
            await SettleAsync();

            Assert.Empty(await states.GetAllAsync(TestContext.Current.CancellationToken));
            Assert.Equal(PlaybackStatus.InProgress, card.Status);
        }

        [Fact]
        public async Task AlsUngehoert_LoeschtDenStandUndSetztDieAnzeigeZurueck()
        {
            (NewEpisodeCardViewModel card, FakePlaybackStateDataService states, _) =
                Build(status: PlaybackStatus.Finished, progressPercent: 100);

            card.MarkAsPlayedCommand.Execute(null);
            await ChangeSignals.WaitForAsync(card, () => card.Status == PlaybackStatus.Finished, "Folge gilt als gehört");
            card.MarkAsUnplayedCommand.Execute(null);
            await ChangeSignals.WaitForAsync(card, () => card.Status == PlaybackStatus.NotStarted, "Folge gilt wieder als ungehört");

            Assert.Empty(await states.GetAllAsync(TestContext.Current.CancellationToken));
            Assert.Equal(PlaybackStatus.NotStarted, card.Status);
            Assert.Equal(0, card.ProgressPercent);
        }

        [Fact]
        public async Task AlsUngehoert_BleibtNachAblehnungAus()
        {
            (NewEpisodeCardViewModel card, FakePlaybackStateDataService states, _) =
                Build(status: PlaybackStatus.Finished, confirmResult: false);

            card.MarkAsUnplayedCommand.Execute(null);
            await SettleAsync();

            Assert.Empty(await states.GetAllAsync(TestContext.Current.CancellationToken));
            Assert.Equal(PlaybackStatus.Finished, card.Status);
        }
    }
}
