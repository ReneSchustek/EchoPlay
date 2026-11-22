using EchoPlay.App.Models;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für die Kachel einer Neuerscheinung: Beschriftung, Kennzeichen und die
    /// drei Aktionen (abspielen, als gehört, als ungehört).
    /// </summary>
    /// <remarks>
    /// Die Farbe des Kennzeichens kommt aus der Palette und bleibt ohne laufendes
    /// Fenster leer; geprüft werden Text und Sichtbarkeit, die davon unabhängig sind.
    /// </remarks>
    public sealed class NewEpisodeCardViewModelTests
    {
        private static readonly DateTime Today = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>Einstellungen für <see cref="Build"/>; ohne Angabe gelten die Vorgaben.</summary>
        private sealed record CardSetup
        {
            public string EpisodeTitle { get; init; } = "Folge 1";
            public bool HasLocalTrack { get; init; } = true;
            public bool IsAnnounced { get; init; }
            public int? EpisodeNumber { get; init; }
            public DateTime? ReleaseDate { get; init; }
            public PlaybackStatus Status { get; init; } = PlaybackStatus.NotStarted;
            public double ProgressPercent { get; init; }
            public bool ConfirmResult { get; init; } = true;
            public string SeriesName { get; init; } = "TKKG";
        }

        private static (NewEpisodeCardViewModel Card, FakePlaybackStateDataService States, FakeErrorDialogService Errors)
            Build(CardSetup? setup = null)
        {
            setup ??= new CardSetup();
            FakePlaybackStateDataService states = new();
            FakeErrorDialogService errors = new();

            ServiceCollection services = new();
            _ = services.AddScoped<IPlaybackStateDataService>(_ => states);
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<ILocalTrackDataService>(_ => new FakeLocalTrackDataService());
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService());

            ServiceProvider provider = services.BuildServiceProvider();

            NewEpisodeCardData data = new()
            {
                EpisodeId = Guid.NewGuid(),
                SeriesId = Guid.NewGuid(),
                SeriesName = setup.SeriesName,
                EpisodeTitle = setup.EpisodeTitle,
                Status = setup.Status,
                ProgressPercent = setup.ProgressPercent,
                HasLocalTrack = setup.HasLocalTrack,
                IsAnnounced = setup.IsAnnounced,
                EpisodeNumber = setup.EpisodeNumber,
                ReleaseDate = setup.ReleaseDate,
            };
            NewEpisodeCardViewModel card = new(
                data,
                coverImage: null,
                provider.GetRequiredService<IServiceScopeFactory>(),
                new NewEpisodeCardServices(errors, new FakeConfirmationDialogService(setup.ConfirmResult), new FakePlayerService(), null),
                new FakeClock { UtcNow = Today });

            return (card, states, errors);
        }

        /// <summary>
        /// Lässt einen abgelehnten Befehl auslaufen. Bei einer Ablehnung ändert sich
        /// nichts, worauf sich warten ließe, deshalb ein paar Durchläufe der
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
        public void Title_LosesSeriesNameAndEpisodeNumber()
        {
            // Der Albumname des Anbieters wiederholt Serie und Nummer. Auf der Kachel
            // stünde dann dreimal dasselbe.
            (NewEpisodeCardViewModel card, _, _) = Build(new()
            {
                EpisodeTitle = "TKKG - Folge 26 - Zusammengewachsen",
            });

            Assert.Equal("Zusammengewachsen", card.EpisodeTitle);
        }

        [Fact]
        public void Title_StaysWhenNothingWouldRemain()
        {
            (NewEpisodeCardViewModel card, _, _) = Build(new() { EpisodeTitle = "TKKG", SeriesName = "TKKG" });

            Assert.Equal("TKKG", card.EpisodeTitle);
        }

        [Fact]
        public void EpisodeNumber_AppearsOnlyWhenKnown()
        {
            (NewEpisodeCardViewModel withNumber, _, _) = Build(new() { EpisodeNumber = 229 });
            (NewEpisodeCardViewModel withoutNumber, _, _) = Build();

            Assert.Equal("Folge 229", withNumber.EpisodeNumberText);
            Assert.Null(withoutNumber.EpisodeNumberText);
        }

        [Fact]
        public void InfoLine_NamesNumberDateAndOrigin()
        {
            (NewEpisodeCardViewModel card, _, _) = Build(new()
            {
                HasLocalTrack = false,
                EpisodeNumber = 170,
                ReleaseDate = new DateTime(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc),
            });

            Assert.Equal("Nr. 170 · 14.08.2026 · online", card.InfoLineText);
            Assert.Equal(Visibility.Visible, card.InfoLineVisibility);
            Assert.True(card.IsOnlineOnly);
        }

        [Fact]
        public void InfoLine_WithoutReleaseDate_StaysOff()
        {
            (NewEpisodeCardViewModel card, _, _) = Build(new() { EpisodeNumber = 5 });

            Assert.Null(card.InfoLineText);
            Assert.Equal(Visibility.Collapsed, card.InfoLineVisibility);
        }

        [Fact]
        public void Announcement_ShowsDateAndBadge()
        {
            (NewEpisodeCardViewModel card, _, _) = Build(new()
            {
                HasLocalTrack = false,
                IsAnnounced = true,
                EpisodeNumber = 26,
                ReleaseDate = Today.AddDays(30),
            });

            Assert.Equal("17.09.2026", card.ReleaseDateText);
            Assert.Equal(Visibility.Visible, card.ReleaseDateVisibility);
            Assert.Equal("Angekündigt", card.BadgeText);
            Assert.Equal(Visibility.Visible, card.BadgeVisibility);
            Assert.Contains("angekündigt", card.InfoLineText, StringComparison.Ordinal);
        }

        [Fact]
        public void FreshEpisode_CarriesTheNewBadge()
        {
            (NewEpisodeCardViewModel card, _, _) = Build(new() { ReleaseDate = Today.AddDays(-3) });

            Assert.Equal("Neu", card.BadgeText);
            Assert.Equal(Visibility.Visible, card.BadgeVisibility);
            Assert.Null(card.ReleaseDateText);
        }

        [Fact]
        public void OlderEpisode_CarriesNoBadge()
        {
            (NewEpisodeCardViewModel card, _, _) = Build(new() { ReleaseDate = Today.AddDays(-30) });

            Assert.Null(card.BadgeText);
            Assert.Equal(Visibility.Collapsed, card.BadgeVisibility);
            Assert.Null(card.BadgeBrush);
        }

        [Fact]
        public void ProgressBar_AppearsOnlyForStartedEpisodes()
        {
            (NewEpisodeCardViewModel inProgress, _, _) = Build(new()
            {
                Status = PlaybackStatus.InProgress,
                ProgressPercent = 42,
            });
            (NewEpisodeCardViewModel finished, _, _) = Build(new() { Status = PlaybackStatus.Finished });

            Assert.Equal(Visibility.Visible, inProgress.ProgressBarVisibility);
            Assert.Equal(Visibility.Collapsed, inProgress.CompletedCheckVisibility);
            Assert.Equal(42, inProgress.ProgressPercent);
            Assert.Equal(Visibility.Visible, finished.CompletedCheckVisibility);
            Assert.Equal(Visibility.Collapsed, finished.ProgressBarVisibility);
        }

        [Fact]
        public void CoverSwap_IgnoresAnEmptyImage()
        {
            // Ein fehlgeschlagener Nachlade-Versuch darf das bereits gezeigte Serien-Cover
            // nicht durch eine Lücke ersetzen.
            (NewEpisodeCardViewModel card, _, _) = Build();

            card.UpdateCoverImage(null);

            Assert.False(card.HasEpisodeCover);
            Assert.Null(card.CoverImage);
        }

        [Fact]
        public void CoverRelease_ResetsTheFlag()
        {
            (NewEpisodeCardViewModel card, _, _) = Build();

            card.ClearCoverImage();

            Assert.False(card.HasEpisodeCover);
            Assert.Null(card.CoverImage);
        }

        [Fact]
        public void AutomationNames_NameTheEpisode()
        {
            (NewEpisodeCardViewModel card, _, _) = Build(new() { EpisodeTitle = "Der Superhund" });

            Assert.Contains("Der Superhund", card.PlayAutomationName, StringComparison.Ordinal);
            Assert.Contains("Der Superhund", card.ActionsAutomationName, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Play_ReportsAnEpisodeNotYetReleased()
        {
            (NewEpisodeCardViewModel card, _, FakeErrorDialogService errors) = Build(new()
            {
                HasLocalTrack = false,
                IsAnnounced = true,
                ReleaseDate = Today.AddDays(10),
            });

            card.PlayCommand.Execute(null);
            _ = await errors.FirstDialogShown.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            (string Title, string Message) dialog = Assert.Single(errors.ShownDialogs);
            Assert.Equal("Noch nicht verfügbar", dialog.Title);
        }

        [Fact]
        public async Task MarkPlayed_WritesTheStateAndUpdatesTheDisplay()
        {
            (NewEpisodeCardViewModel card, FakePlaybackStateDataService states, _) =
                Build(new() { Status = PlaybackStatus.InProgress, ProgressPercent = 30 });

            card.MarkAsPlayedCommand.Execute(null);
            await ChangeSignals.WaitForAsync(card, () => card.Status == PlaybackStatus.Finished, "Folge gilt als gehört");

            IReadOnlyList<PlaybackState> stored = await states.GetAllAsync(TestContext.Current.CancellationToken);
            Assert.True(Assert.Single(stored).IsCompleted);
            Assert.Equal(PlaybackStatus.Finished, card.Status);
            Assert.Equal(100, card.ProgressPercent);
        }

        [Fact]
        public async Task MarkPlayed_AfterDecline_DoesNothing()
        {
            (NewEpisodeCardViewModel card, FakePlaybackStateDataService states, _) =
                Build(new() { Status = PlaybackStatus.InProgress, ProgressPercent = 30, ConfirmResult = false });

            card.MarkAsPlayedCommand.Execute(null);
            await SettleAsync();

            Assert.Empty(await states.GetAllAsync(TestContext.Current.CancellationToken));
            Assert.Equal(PlaybackStatus.InProgress, card.Status);
        }

        [Fact]
        public async Task MarkUnplayed_DeletesTheStateAndResetsTheDisplay()
        {
            (NewEpisodeCardViewModel card, FakePlaybackStateDataService states, _) =
                Build(new() { Status = PlaybackStatus.Finished, ProgressPercent = 100 });

            card.MarkAsPlayedCommand.Execute(null);
            await ChangeSignals.WaitForAsync(card, () => card.Status == PlaybackStatus.Finished, "Folge gilt als gehört");
            card.MarkAsUnplayedCommand.Execute(null);
            await ChangeSignals.WaitForAsync(card, () => card.Status == PlaybackStatus.NotStarted, "Folge gilt wieder als ungehört");

            Assert.Empty(await states.GetAllAsync(TestContext.Current.CancellationToken));
            Assert.Equal(PlaybackStatus.NotStarted, card.Status);
            Assert.Equal(0, card.ProgressPercent);
        }

        [Fact]
        public async Task MarkUnplayed_AfterDecline_DoesNothing()
        {
            (NewEpisodeCardViewModel card, FakePlaybackStateDataService states, _) =
                Build(new() { Status = PlaybackStatus.Finished, ConfirmResult = false });

            card.MarkAsUnplayedCommand.Execute(null);
            await SettleAsync();

            Assert.Empty(await states.GetAllAsync(TestContext.Current.CancellationToken));
            Assert.Equal(PlaybackStatus.Finished, card.Status);
        }
    }
}
