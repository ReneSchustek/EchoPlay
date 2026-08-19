using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Spotify.Auth;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Sammelvorgänge der Online-Mediathek: alle Serien auf neue Folgen prüfen,
    /// eine Serie entfernen und die Überwachung umschalten.
    /// </summary>
    /// <remarks>
    /// Der Sammelabruf spricht für jede Serie einen fremden Dienst an. Drei Zusagen hängen
    /// daran: Ohne Netzfreigabe geht nichts hinaus, eine Serie, die scheitert, hält die
    /// übrigen nicht auf, und zwischen zwei Abrufen wird gedrosselt — sonst sperrt der
    /// Anbieter nach wenigen Serien aus.
    /// <para>
    /// Eigene Datei statt Ergänzung von <c>OnlineSubActionsTests</c>: Die Datei liegt
    /// bereits über der Grenze aus <c>testing.md</c>.
    /// </para>
    /// </remarks>
    public sealed class OnlineBulkRefreshActionsTests
    {
        private static readonly Guid UnknownSeriesId = new("00000000-0000-0000-0000-beef00000001");

        // ── Sammelabruf ──────────────────────────────────────────────────────────

        [Fact]
        public async Task RefreshAll_WhenOnlineAccessIsDeclined_ChecksNothing()
        {
            Harness harness = await Harness.BuildAsync(
                series: [OnlineSeries("Die drei Fragezeichen", "spotify-1")],
                allowOnlineAccess: false);

            await harness.Actions.RefreshAllOnlineSeriesAsync();

            // „Nur offline" ist eine Entscheidung des Anwenders. Ein Sammelabruf, der sie
            // übergeht, meldet jede Serie des Bestands an einen fremden Dienst.
            Assert.Empty(harness.ImportSource.RequestedSeriesIds);
            Assert.Empty(harness.LoadingStates);
        }

        [Fact]
        public async Task RefreshAll_ChecksOnlyImportedSeries()
        {
            Harness harness = await Harness.BuildAsync(series:
            [
                OnlineSeries("Aus dem Netz", "spotify-1"),
                LocalSeries("Von der Platte"),
            ]);

            await harness.Actions.RefreshAllOnlineSeriesAsync();

            // Eine Serie, die von der Platte gelesen wurde, hat beim Anbieter nichts zu
            // suchen — die Abfrage fände nichts und kostete nur ein Zeitfenster.
            Assert.Equal(["spotify-1"], harness.ImportSource.RequestedSeriesIds);
        }

        [Fact]
        public async Task RefreshAll_WhenOneSeriesFails_KeepsCheckingTheOthers()
        {
            Harness harness = await Harness.BuildAsync(
                series:
                [
                    OnlineSeries("Erste", "spotify-kaputt"),
                    OnlineSeries("Zweite", "spotify-heil"),
                ],
                failForSourceSeriesId: "spotify-kaputt");

            await harness.Actions.RefreshAllOnlineSeriesAsync();

            // Ein nicht erreichbarer Anbieter für eine Serie ist der Regelfall bei einem
            // Bestand von hundert. Bräche der Lauf dort ab, prüfte er nie mehr als bis zur
            // ersten schlechten Serie.
            Assert.Equal(["spotify-kaputt", "spotify-heil"], harness.ImportSource.RequestedSeriesIds);
            Assert.True(harness.Reloaded);
        }

        [Fact]
        public async Task RefreshAll_ThrottlesBetweenSeriesButNotAfterTheLast()
        {
            Harness harness = await Harness.BuildAsync(series:
            [
                OnlineSeries("Erste", "spotify-1"),
                OnlineSeries("Zweite", "spotify-2"),
                OnlineSeries("Dritte", "spotify-3"),
            ]);

            await harness.Actions.RefreshAllOnlineSeriesAsync();

            // Drei Serien, zwei Pausen. Eine Pause hinter der letzten wäre reine Wartezeit
            // vor einer Ansicht, die längst fertig ist.
            Assert.Equal(2, harness.RateLimiter.Waits.Count);
        }

        [Fact]
        public async Task RefreshAll_ReportsProgressForEverySeries()
        {
            Harness harness = await Harness.BuildAsync(series:
            [
                OnlineSeries("Erste", "spotify-1"),
                OnlineSeries("Zweite", "spotify-2"),
            ]);

            await harness.Actions.RefreshAllOnlineSeriesAsync();

            // Der Text nennt Nummer, Gesamtzahl und Titel. Ohne ihn steht der Anwender bei
            // hundert Serien minutenlang vor einem stummen Ladebalken.
            Assert.Contains("1 von 2: Erste", harness.StatusTexts);
            Assert.Contains("2 von 2: Zweite", harness.StatusTexts);
        }

        [Fact]
        public async Task RefreshAll_ClearsTheStatusTextWhenDone()
        {
            Harness harness = await Harness.BuildAsync(series: [OnlineSeries("Erste", "spotify-1")]);

            await harness.Actions.RefreshAllOnlineSeriesAsync();

            // Bliebe „3 von 100" stehen, sähe die fertige Ansicht aus wie eine hängende.
            Assert.Equal(string.Empty, harness.StatusTexts[^1]);
        }

        [Fact]
        public async Task RefreshAll_ReleasesTheLoadingState()
        {
            Harness harness = await Harness.BuildAsync(series: [OnlineSeries("Erste", "spotify-1")]);

            await harness.Actions.RefreshAllOnlineSeriesAsync();

            Assert.Equal([true, false], harness.LoadingStates);
        }

        [Fact]
        public async Task RefreshAll_WithoutAnySeries_StillReleasesTheLoadingState()
        {
            Harness harness = await Harness.BuildAsync(series: []);

            await harness.Actions.RefreshAllOnlineSeriesAsync();

            Assert.Equal([true, false], harness.LoadingStates);
            Assert.True(harness.Reloaded);
        }

        // ── Serie entfernen ──────────────────────────────────────────────────────

        [Fact]
        public async Task RemoveSeries_WithUnknownId_AsksNothing()
        {
            Harness harness = await Harness.BuildAsync(series: [OnlineSeries("Bleibt", "spotify-1")]);

            await harness.Actions.RemoveSeriesAsync(UnknownSeriesId);

            // Eine Rückfrage zu einer Serie, die es nicht gibt, nennt keinen Titel — der
            // Anwender bestätigte etwas Unbenanntes.
            Assert.Equal(0, harness.Confirmation.CallCount);
            Assert.Equal(1, harness.Actions.RemoveSeriesCallCount);
        }

        [Fact]
        public async Task RemoveSeries_WhenUserDeclines_KeepsTheSeries()
        {
            Series series = OnlineSeries("Bleibt", "spotify-1");
            Harness harness = await Harness.BuildAsync(series: [series], confirmResult: false);

            await harness.Actions.RemoveSeriesAsync(series.Id);

            Assert.Equal(1, harness.Confirmation.CallCount);
            _ = Assert.Single(harness.SeriesService.All);
            _ = Assert.Single(harness.SeriesVM.AllSeries);
        }

        [Fact]
        public async Task RemoveSeries_WhenConfirmed_RemovesItFromViewAndStore()
        {
            Series series = OnlineSeries("Verschwindet", "spotify-1");
            Harness harness = await Harness.BuildAsync(series: [series]);

            await harness.Actions.RemoveSeriesAsync(series.Id);

            Assert.Empty(harness.SeriesService.All);
            Assert.Empty(harness.SeriesVM.AllSeries);
        }

        // ── Überwachung ──────────────────────────────────────────────────────────

        [Fact]
        public async Task ToggleWatch_WithoutService_DoesNothing()
        {
            Series series = OnlineSeries("Die drei Fragezeichen", "spotify-1");
            Harness harness = await Harness.BuildAsync(series: [series], watchToggleService: null);

            await harness.Actions.ToggleWatchAsync(series.Id, watch: true);

            Assert.Equal(1, harness.Actions.ToggleWatchCallCount);
        }

        [Fact]
        public async Task ToggleWatch_WithService_UpdatesTheTile()
        {
            Series series = OnlineSeries("Die drei Fragezeichen", "spotify-1");
            FakeWatchToggleService watchToggle = new();
            Harness harness = await Harness.BuildAsync(series: [series], watchToggleService: watchToggle);

            await harness.Actions.ToggleWatchAsync(series.Id, watch: true);

            // Die Kachel trägt das Zeichen für „wird überwacht". Bliebe es aus, drückte der
            // Anwender ein zweites Mal und schaltete die Überwachung wieder ab.
            Assert.Equal([(series.Id, true)], watchToggle.Calls);
            Assert.True(harness.SeriesVM.Series[0].IsWatched);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static Series OnlineSeries(string title, string spotifyArtistId) =>
            new()
            {
                Title = title,
                IsOnlineImported = true,
                SpotifyArtistId = spotifyArtistId,
            };

        private static Series LocalSeries(string title) =>
            new()
            {
                Title = title,
                IsOnlineImported = false,
                LocalFolderPath = @"D:\Media",
            };

        /// <summary>
        /// Baut die Sammelvorgänge samt Umfeld. Zusammengefasst, weil die Tests auf mehrere
        /// Nachbauten und auf beide Zustandsrückrufe zugreifen.
        /// </summary>
        private sealed class Harness
        {
            public OnlineBulkRefreshActions Actions { get; private set; } = null!;

            public required OnlineSeriesViewModel SeriesVM { get; init; }

            public required FakeSeriesDataService SeriesService { get; init; }

            public required FakeEpisodeImportSource ImportSource { get; init; }

            public required RecordingHostRateLimiter RateLimiter { get; init; }

            public required FakeConfirmationDialogService Confirmation { get; init; }

            public List<bool> LoadingStates { get; } = [];

            public List<string> StatusTexts { get; } = [];

            public bool Reloaded { get; private set; }

            public static async Task<Harness> BuildAsync(
                IReadOnlyList<Series>? series = null,
                bool allowOnlineAccess = true,
                bool confirmResult = true,
                string? failForSourceSeriesId = null,
                FakeWatchToggleService? watchToggleService = null)
            {
                IReadOnlyList<Series> allSeries = series ?? [];

                FakeSeriesDataService seriesService = new();
                foreach (Series entry in allSeries)
                {
                    await seriesService.AddAsync(entry, TestContext.Current.CancellationToken);
                }

                FakeEpisodeImportSource importSource = new([], failForSourceSeriesId);
                RecordingHostRateLimiter rateLimiter = new();
                FakeConfirmationDialogService confirmation = new(confirmResult);

                ServiceCollection services = new();
                _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
                _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
                _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
                _ = services.AddScoped<ICoverCopyService>(_ => new FakeCoverCopyService());
                _ = services.AddKeyedScoped<IEpisodeImportSource>("Spotify", (_, _) => importSource);
                _ = services.AddKeyedScoped<IEpisodeImportSource>("AppleMusic", (_, _) => importSource);
                _ = services.AddSingleton<ISpotifyClientCredentialsProvider>(
                    FakeSpotifyClientCredentialsProvider.WithCredentials());
                _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(new FakeLoggerFactory());
                _ = services.AddSingleton<IClock>(new FakeClock());
                _ = services.AddHttpClient();
                _ = services.AddSingleton<CoverService>();
                _ = services.AddSingleton<ICoverService>(sp => sp.GetRequiredService<CoverService>());
                _ = services.AddSingleton<ICoverDownloader>(new FakeCoverDownloader());
                _ = services.AddSingleton<EpisodeCoverCacheService>();

                ServiceProvider provider = services.BuildServiceProvider();
                IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

                OnlineLibraryActionsContext context = new(
                    ScopeFactory: scopeFactory,
                    ConfirmationDialogService: confirmation,
                    ImportService: new ImportService(
                        scopeFactory,
                        provider.GetRequiredService<EpisodeCoverCacheService>(),
                        provider.GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>()),
                    ErrorDialogService: new FakeErrorDialogService(),
                    // Der Fortschrittstext hat Platzhalter — als Muster mitgegeben, sonst
                    // verschluckt der Schlüssel die eingesetzten Werte.
                    LocalizationService: new FakeLocalizationService(new Dictionary<string, string>
                    {
                        ["OnlineRefreshProgressText"] = "{0} von {1}: {2}",
                    }),
                    OnlineAccessGuard: new FakeOnlineAccessGuard(allowOnlineAccess),
                    CoverCacheService: null,
                    CoverService: provider.GetRequiredService<CoverService>(),
                    BackgroundCoverService: null,
                    WatchToggleService: watchToggleService,
                    CoverDownloader: provider.GetRequiredService<ICoverDownloader>(),
                    RateLimiter: rateLimiter);

                OnlineSeriesViewModel seriesVM = new();
                List<SeriesCardViewModel> cards = new(allSeries.Count);
                foreach (Series entry in allSeries)
                {
                    cards.Add(new SeriesCardViewModel(
                        entry.Id, entry.Title, coverImage: null,
                        totalEpisodeCount: 0, newEpisodeCount: 0, inProgressCount: 0, finishedCount: 0,
                        isSubscribed: true, isFavorite: false, isWatched: false,
                        scopeFactory, confirmation, new FakeLocalizationService()));
                }

                seriesVM.SetAllSeries(cards);

                Harness harness = new()
                {
                    SeriesVM = seriesVM,
                    SeriesService = seriesService,
                    ImportSource = importSource,
                    RateLimiter = rateLimiter,
                    Confirmation = confirmation,
                };

                harness.Actions = new OnlineBulkRefreshActions(
                    context,
                    seriesVM,
                    new OnlineEpisodesViewModel(),
                    setIsLoading: state => harness.LoadingStates.Add(state),
                    setLoadingStatusText: text => harness.StatusTexts.Add(text),
                    reloadAfterRefreshAsync: () =>
                    {
                        harness.Reloaded = true;
                        return Task.CompletedTask;
                    });

                return harness;
            }
        }
    }
}
