using EchoPlay.App.Models;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für <see cref="DashboardViewModel"/>.
    /// Prüft das Laden von Neuerscheinungen (aus iTunes), Favoriten, Weiterhören und Ankündigungen.
    /// Serien ohne Coverbild, damit keine WinUI-BitmapImage-Instanzen entstehen.
    /// </summary>
    public sealed class DashboardViewModelTests
    {
        private static DashboardViewModel BuildViewModel(
            FakeSeriesDataService seriesService,
            FakeEpisodeDataService episodeService,
            FakePlaybackStateDataService? stateService = null,
            FakeOnlineEpisodeChecker? checker = null,
            FakeCachedNewReleaseDataService? cacheService = null,
            AppSettings? appSettings = null)
        {
            ServiceCollection services = new();
            services.AddScoped<ISeriesDataService>(_ => seriesService);
            services.AddScoped<IEpisodeDataService>(_ => episodeService);
            services.AddScoped<IPlaybackStateDataService>(_ => stateService ?? new FakePlaybackStateDataService());
            services.AddScoped<IDashboardPositionDataService>(_ => new FakeDashboardPositionDataService());
            services.AddScoped<IAppSettingsDataService>(_ => new FakeAppSettingsDataService(appSettings));
            services.AddScoped<IOnlineEpisodeChecker>(_ => checker ?? new FakeOnlineEpisodeChecker());
            services.AddScoped<ICachedNewReleaseDataService>(_ => cacheService ?? new FakeCachedNewReleaseDataService());

            ServiceProvider provider = services.BuildServiceProvider();

            return new DashboardViewModel(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeErrorDialogService(),
                new FakeConfirmationDialogService(),
                new FakePlayerService(),
                new FakeLoggerFactory());
        }

        [Fact]
        public async Task LoadAsync_SetsIsLoadingDuringLoad()
        {
            // IsLoading muss mindestens einmal auf true gewechselt sein, solange LoadAsync läuft
            FakeSeriesDataService seriesService = new();
            DashboardViewModel vm = BuildViewModel(seriesService, new FakeEpisodeDataService());

            bool loadingWasTrue = false;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DashboardViewModel.IsLoading) && vm.IsLoading)
                {
                    loadingWasTrue = true;
                }
            };

            await vm.LoadAsync();

            Assert.True(loadingWasTrue);
            // Nach dem Laden muss die Anzeige wieder verschwinden
            Assert.False(vm.IsLoading);
        }

        [Fact]
        public async Task LoadAsync_NoFavoriteSeries_NewEpisodesEmpty()
        {
            // Ohne Favoriten bleiben Neuerscheinungen leer (iTunes wird nur für Favoriten abgefragt)
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series { Title = "TKKG", IsSubscribed = true, IsFavorite = false });

            DashboardViewModel vm = BuildViewModel(seriesService, episodeService);
            await vm.LoadAsync();

            // Neuerscheinungen kommen aus dem DB-Cache (leer bei leerem Fake)
            Assert.Empty(vm.NewEpisodeGroups);
        }

        [Fact]
        public async Task LoadAsync_FavoriteSeries_ShownInFavoriteSection()
        {
            // Favorisierte Serien erscheinen in der Favoriten-Kachelreihe
            FakeSeriesDataService seriesService = new();

            await seriesService.AddAsync(new Series { Title = "TKKG", IsSubscribed = true, IsFavorite = true });
            await seriesService.AddAsync(new Series { Title = "Fünf Freunde", IsSubscribed = true, IsFavorite = false });

            DashboardViewModel vm = BuildViewModel(seriesService, new FakeEpisodeDataService());
            await vm.LoadAsync();

            Assert.Single(vm.FavoriteSeries);
            Assert.Equal("TKKG", vm.FavoriteSeries[0].SeriesName);
        }

        [Fact]
        public async Task LoadAsync_NoFavorites_ShowsNoFavoritesHint()
        {
            // Wenn Serien abonniert aber keine favorisiert → Hinweistext sichtbar
            FakeSeriesDataService seriesService = new();
            await seriesService.AddAsync(new Series { Title = "TKKG", IsSubscribed = true, IsFavorite = false });

            DashboardViewModel vm = BuildViewModel(seriesService, new FakeEpisodeDataService());
            await vm.LoadAsync();

            Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, vm.NoFavoritesHintVisibility);
        }

        [Fact]
        public async Task LoadAsync_WithCachedReleases_ShowsNewReleases()
        {
            // Gecachte Neuerscheinungen werden sofort als Kacheln angezeigt
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series { Title = "TKKG", IsSubscribed = true, IsFavorite = true, IsWatched = true });
            Series series = seriesService.All[0];

            // DB-Cache vorbefüllen
            FakeCachedNewReleaseDataService cacheService = new(
            [
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Series = series,
                    Title = "TKKG - Folge 230 - Der Spion",
                    EpisodeNumber = 230,
                    ReleaseDate = DateTime.UtcNow.AddDays(-5),
                    CollectionId = 12345,
                    CheckedAtUtc = DateTime.UtcNow
                }
            ]);

            DashboardViewModel vm = BuildViewModel(seriesService, episodeService, cacheService: cacheService);
            await vm.LoadAsync();

            // Kacheln werden synchron aus dem Cache gebaut – kein Delay nötig.
            // Die Gruppierung ist jetzt nach Monat, nicht nach Serie.
            Assert.Single(vm.NewEpisodeGroups);
            Assert.Single(vm.NewEpisodeGroups[0].Episodes);
            Assert.Equal("TKKG", vm.NewEpisodeGroups[0].Episodes[0].SeriesName);
        }

        [Fact]
        public async Task LoadAsync_CompletedEpisodes_NotInNewEpisodes()
        {
            // Bereits gehörte Episoden erscheinen nicht in Neuerscheinungen
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakePlaybackStateDataService stateService = new();

            await seriesService.AddAsync(new Series { Title = "TKKG", IsSubscribed = true, IsFavorite = true, IsWatched = true });
            Guid seriesId = seriesService.All[0].Id;

            // Lokale Episode mit Folgennummer 230 – als gehört markiert
            await episodeService.AddAsync(new Episode
            {
                Title = "Gehörte Folge",
                SeriesId = seriesId,
                EpisodeNumber = 230,
                LocalTrackCount = 1
            });
            Guid episodeId = episodeService.All[0].Id;

            await stateService.AddAsync(new PlaybackState
            {
                EpisodeId = episodeId,
                IsCompleted = true,
                LastPosition = TimeSpan.FromMinutes(60)
            });

            // DB-Cache kennt die gleiche Folge 230
            Series series = seriesService.All[0];
            FakeCachedNewReleaseDataService cacheService = new(
            [
                new CachedNewRelease
                {
                    SeriesId = seriesId,
                    Series = series,
                    Title = "TKKG - Folge 230",
                    EpisodeNumber = 230,
                    ReleaseDate = DateTime.UtcNow.AddDays(-5),
                    CollectionId = 12345,
                    CheckedAtUtc = DateTime.UtcNow
                }
            ]);

            DashboardViewModel vm = BuildViewModel(seriesService, episodeService, stateService, cacheService: cacheService);
            await vm.LoadAsync();

            // Gehörte Folge wurde gefiltert – keine Neuerscheinungen übrig
            Assert.Empty(vm.NewEpisodeGroups);
        }

        [Fact]
        public async Task LoadAsync_GroupsAnnouncedSeparatelyFromMonths()
        {
            // Erschienene Folgen → Monatsgruppe; angekündigte → "Angekündigt"-Gruppe
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series { Title = "Die drei Fragezeichen", IsSubscribed = true, IsFavorite = true, IsWatched = true });
            Series series = seriesService.All[0];

            // DB-Cache mit einer verfügbaren und einer angekündigten Folge
            FakeCachedNewReleaseDataService cacheService = new(
            [
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Series = series,
                    Title = "Verfügbare Folge",
                    EpisodeNumber = 238,
                    ReleaseDate = DateTime.UtcNow.AddDays(-1),
                    CollectionId = 100,
                    CheckedAtUtc = DateTime.UtcNow
                },
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Series = series,
                    Title = "Angekündigte Folge",
                    EpisodeNumber = 239,
                    ReleaseDate = DateTime.UtcNow.AddDays(7),
                    CollectionId = 101,
                    CheckedAtUtc = DateTime.UtcNow
                }
            ]);

            DashboardViewModel vm = BuildViewModel(seriesService, episodeService, cacheService: cacheService);
            await vm.LoadAsync();

            // Zwei Gruppen: "Angekündigt" und der aktuelle Monat
            Assert.Equal(2, vm.NewEpisodeGroups.Count);

            // "Angekündigt" ist die erste Gruppe (SortKey 0 = ganz oben)
            NewEpisodesGroupViewModel announcedGroup = vm.NewEpisodeGroups[0];
            Assert.Equal("Angekündigt", announcedGroup.GroupLabel);
            Assert.Single(announcedGroup.Episodes);
            Assert.Equal("Angekündigte Folge", announcedGroup.Episodes[0].EpisodeTitle);

            // Monatsgruppe enthält die verfügbare Folge
            NewEpisodesGroupViewModel monthGroup = vm.NewEpisodeGroups[1];
            Assert.Single(monthGroup.Episodes);
            Assert.Equal("Verfügbare Folge", monthGroup.Episodes[0].EpisodeTitle);
        }

        [Fact]
        public async Task LoadAsync_SortsNewEpisodesByReleaseDateDescending()
        {
            // Neueste Folge soll zuerst erscheinen (Cache liefert absteigend sortiert)
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series { Title = "TKKG", IsSubscribed = true, IsFavorite = true, IsWatched = true });
            Series series = seriesService.All[0];

            // DB-Cache: zwei Folgen mit unterschiedlichem Datum
            FakeCachedNewReleaseDataService cacheService = new(
            [
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Series = series,
                    Title = "Folge Neu",
                    EpisodeNumber = 231,
                    ReleaseDate = DateTime.UtcNow.AddDays(-1),
                    CollectionId = 200,
                    CheckedAtUtc = DateTime.UtcNow
                },
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Series = series,
                    Title = "Folge Alt",
                    EpisodeNumber = 230,
                    ReleaseDate = DateTime.UtcNow.AddDays(-5),
                    CollectionId = 201,
                    CheckedAtUtc = DateTime.UtcNow
                }
            ]);

            DashboardViewModel vm = BuildViewModel(seriesService, episodeService, cacheService: cacheService);
            await vm.LoadAsync();

            List<NewEpisodeCardViewModel> episodes = vm.NewEpisodeGroups.SelectMany(g => g.Episodes).ToList();
            Assert.Equal(2, episodes.Count);
            Assert.Equal("Folge Neu", episodes[0].EpisodeTitle);
            Assert.Equal("Folge Alt", episodes[1].EpisodeTitle);
        }

        [Fact]
        public async Task LoadAsync_MultipleMonths_CreatesGroupPerMonth()
        {
            // Einträge aus verschiedenen Monaten → je eine Monatsgruppe
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series { Title = "TKKG", IsSubscribed = true, IsWatched = true });
            Series series = seriesService.All[0];

            FakeCachedNewReleaseDataService cacheService = new(
            [
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Series = series,
                    Title = "März-Folge",
                    EpisodeNumber = 231,
                    ReleaseDate = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
                    CollectionId = 300,
                    CheckedAtUtc = DateTime.UtcNow
                },
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Series = series,
                    Title = "Februar-Folge",
                    EpisodeNumber = 230,
                    ReleaseDate = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc),
                    CollectionId = 301,
                    CheckedAtUtc = DateTime.UtcNow
                }
            ]);

            DashboardViewModel vm = BuildViewModel(seriesService, episodeService, cacheService: cacheService);
            await vm.LoadAsync();

            // Zwei Monatsgruppen: März und Februar (neuester zuerst)
            Assert.Equal(2, vm.NewEpisodeGroups.Count);
            Assert.Contains("März", vm.NewEpisodeGroups[0].GroupLabel);
            Assert.Contains("Februar", vm.NewEpisodeGroups[1].GroupLabel);
        }

        [Fact]
        public void Badge_AnnouncedEpisode_ShowsAnnouncedBadge()
        {
            // Episode mit Datum in der Zukunft → Badge "Angekündigt"
            NewEpisodeCardViewModel card = BuildCard(
                isAnnounced: true,
                releaseDate: DateTime.UtcNow.AddDays(7));

            Assert.Equal("Angekündigt", card.BadgeText);
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, card.BadgeVisibility);
        }

        [Fact]
        public void Badge_RecentEpisode_ShowsNeuBadge()
        {
            // Episode vor 3 Tagen erschienen → Badge "Neu"
            NewEpisodeCardViewModel card = BuildCard(
                isAnnounced: false,
                releaseDate: DateTime.UtcNow.AddDays(-3));

            Assert.Equal("Neu", card.BadgeText);
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, card.BadgeVisibility);
        }

        [Fact]
        public void Badge_OlderEpisode_NoBadge()
        {
            // Episode vor 30 Tagen erschienen → kein Badge
            NewEpisodeCardViewModel card = BuildCard(
                isAnnounced: false,
                releaseDate: DateTime.UtcNow.AddDays(-30));

            Assert.Null(card.BadgeText);
            Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, card.BadgeVisibility);
        }

        [Fact]
        public void InfoLine_AnnouncedEpisode_ShowsNumberDateAndLabel()
        {
            // Angekündigte Episode zeigt "Nr. 239 · dd.MM.yyyy · angekündigt"
            DateTime future = DateTime.UtcNow.AddDays(14);
            NewEpisodeCardViewModel card = BuildCard(
                isAnnounced: true,
                releaseDate: future,
                episodeNumber: 239);

            Assert.Contains("Nr. 239", card.InfoLineText!);
            Assert.Contains(future.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture), card.InfoLineText!);
            Assert.Contains("angekündigt", card.InfoLineText!);
        }

        [Fact]
        public async Task LoadAsync_OfflineMode_SkipsNewReleases()
        {
            // Im Offline-Modus: Neuerscheinungen werden nicht angezeigt, auch wenn Cache gefüllt
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series { Title = "TKKG", IsSubscribed = true, IsWatched = true });
            Series series = seriesService.All[0];

            FakeCachedNewReleaseDataService cacheService = new(
            [
                new CachedNewRelease
                {
                    SeriesId = series.Id,
                    Series = series,
                    Title = "TKKG - Folge 230",
                    EpisodeNumber = 230,
                    ReleaseDate = DateTime.UtcNow.AddDays(-5),
                    CollectionId = 99999,
                    CheckedAtUtc = DateTime.UtcNow
                }
            ]);

            AppSettings offlineSettings = new() { OfflineMode = true };

            DashboardViewModel vm = BuildViewModel(seriesService, episodeService,
                cacheService: cacheService, appSettings: offlineSettings);
            await vm.LoadAsync();

            // Trotz gefülltem Cache: keine Neuerscheinungen im Offline-Modus
            Assert.Empty(vm.NewEpisodeGroups);
        }

        /// <summary>
        /// Hilfsmethode: erstellt eine Kachel mit minimalen Abhängigkeiten für Badge-/InfoLine-Tests.
        /// </summary>
        private static NewEpisodeCardViewModel BuildCard(
            bool isAnnounced,
            DateTime? releaseDate,
            int? episodeNumber = null,
            string seriesName = "Test-Serie",
            string episodeTitle = "Test-Folge")
        {
            ServiceCollection services = new();
            services.AddScoped<IPlaybackStateDataService>(_ => new FakePlaybackStateDataService());
            ServiceProvider provider = services.BuildServiceProvider();

            return new NewEpisodeCardViewModel(
                episodeId:                  Guid.NewGuid(),
                seriesId:                   Guid.NewGuid(),
                seriesName:                 seriesName,
                episodeTitle:               episodeTitle,
                coverImage:                 null,
                status:                     PlaybackStatus.NotStarted,
                progressPercent:            0,
                hasLocalTrack:              false,
                isAnnounced:                isAnnounced,
                scopeFactory:               provider.GetRequiredService<IServiceScopeFactory>(),
                errorDialogService:         new FakeErrorDialogService(),
                confirmationDialogService:  new FakeConfirmationDialogService(),
                playerService:              new FakePlayerService(),
                episodeNumber:              episodeNumber,
                releaseDate:                releaseDate);
        }

        [Fact]
        public async Task MarkAsPlayed_SetsFinishedStatus()
        {
            // MarkAsPlayedCommand soll Status auf Finished setzen und PlaybackState persistieren
            FakePlaybackStateDataService stateService = new();

            ServiceCollection services = new();
            services.AddScoped<IPlaybackStateDataService>(_ => stateService);
            ServiceProvider provider = services.BuildServiceProvider();

            NewEpisodeCardViewModel card = new(
                episodeId:                  Guid.NewGuid(),
                seriesId:                   Guid.NewGuid(),
                seriesName:                 "TKKG",
                episodeTitle:               "Folge 1",
                coverImage:                 null,
                status:                     PlaybackStatus.NotStarted,
                progressPercent:            0,
                hasLocalTrack:              false,
                isAnnounced:                false,
                scopeFactory:               provider.GetRequiredService<IServiceScopeFactory>(),
                errorDialogService:         new FakeErrorDialogService(),
                confirmationDialogService:  new FakeConfirmationDialogService(result: true),
                playerService:              new FakePlayerService());

            card.MarkAsPlayedCommand.Execute(null);

            // Task.FromResult-Fakes laufen synchron – Status ist sofort gesetzt
            Assert.Equal(PlaybackStatus.Finished, card.Status);
        }

        [Fact]
        public async Task LoadAsync_OnlineEpisodeCompleted_AppearsInRecentSeries()
        {
            // Online-Folgen haben LastPosition = 0 aber IsCompleted = true –
            // diese müssen trotzdem unter "Zuletzt gehört" erscheinen
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakePlaybackStateDataService stateService = new();

            await seriesService.AddAsync(new Series
            {
                Title = "Die drei Fragezeichen",
                IsSubscribed = true
            });
            Guid seriesId = seriesService.All[0].Id;

            await episodeService.AddAsync(new Episode
            {
                Title = "Online-Folge",
                SeriesId = seriesId,
                LocalTrackCount = 0
            });
            Guid episodeId = episodeService.All[0].Id;

            // Online-Folge: kein lokaler Fortschritt, aber als gehört markiert
            await stateService.AddAsync(new PlaybackState
            {
                EpisodeId = episodeId,
                LastPosition = TimeSpan.Zero,
                IsCompleted = true,
                LastPlayedAt = DateTime.UtcNow
            });

            DashboardViewModel vm = BuildViewModel(seriesService, episodeService, stateService);
            await vm.LoadAsync();

            Assert.Single(vm.RecentSeries);
            Assert.Equal("Die drei Fragezeichen", vm.RecentSeries[0].SeriesName);
            Assert.Equal("Online-Folge", vm.RecentSeries[0].LastEpisodeTitle);
        }

        [Fact]
        public async Task LoadAsync_LocalEpisodeWithPosition_StillAppearsInRecentSeries()
        {
            // Sicherstellung: lokal abgespielte Folgen funktionieren weiterhin
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakePlaybackStateDataService stateService = new();

            await seriesService.AddAsync(new Series
            {
                Title = "TKKG",
                IsSubscribed = true
            });
            Guid seriesId = seriesService.All[0].Id;

            await episodeService.AddAsync(new Episode
            {
                Title = "Lokale Folge",
                SeriesId = seriesId,
                LocalTrackCount = 1
            });
            Guid episodeId = episodeService.All[0].Id;

            await stateService.AddAsync(new PlaybackState
            {
                EpisodeId = episodeId,
                LastPosition = TimeSpan.FromMinutes(15),
                IsCompleted = false
            });

            DashboardViewModel vm = BuildViewModel(seriesService, episodeService, stateService);
            await vm.LoadAsync();

            Assert.Single(vm.RecentSeries);
            Assert.Equal("TKKG", vm.RecentSeries[0].SeriesName);
        }

        [Fact]
        public async Task LoadAsync_RecentSeries_SortsByLastPlayedAtFirst()
        {
            // LastPlayedAt hat Vorrang vor UpdatedAt bei der Sortierung
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakePlaybackStateDataService stateService = new();

            await seriesService.AddAsync(new Series { Title = "Serie A", IsSubscribed = true, IsWatched = true });
            await seriesService.AddAsync(new Series { Title = "Serie B", IsSubscribed = true, IsWatched = true });
            Guid seriesAId = seriesService.All[0].Id;
            Guid seriesBId = seriesService.All[1].Id;

            await episodeService.AddAsync(new Episode
            {
                Title = "Folge A",
                SeriesId = seriesAId,
                LocalTrackCount = 0
            });
            await episodeService.AddAsync(new Episode
            {
                Title = "Folge B",
                SeriesId = seriesBId,
                LocalTrackCount = 1
            });
            Guid episodeAId = episodeService.All[0].Id;
            Guid episodeBId = episodeService.All[1].Id;

            // Serie A: älter gehört, aber neueres UpdatedAt
            await stateService.AddAsync(new PlaybackState
            {
                EpisodeId = episodeAId,
                IsCompleted = true,
                LastPlayedAt = DateTime.UtcNow.AddHours(-2)
            });

            // Serie B: neuer gehört, aber älteres UpdatedAt
            await stateService.AddAsync(new PlaybackState
            {
                EpisodeId = episodeBId,
                LastPosition = TimeSpan.FromMinutes(10),
                LastPlayedAt = DateTime.UtcNow.AddMinutes(-30)
            });

            DashboardViewModel vm = BuildViewModel(seriesService, episodeService, stateService);
            await vm.LoadAsync();

            Assert.Equal(2, vm.RecentSeries.Count);
            // Serie B war zuletzt gehört → steht oben
            Assert.Equal("Serie B", vm.RecentSeries[0].SeriesName);
            Assert.Equal("Serie A", vm.RecentSeries[1].SeriesName);
        }

        // ── CleanEpisodeTitle ──────────────────────────────────────────────

        [Fact]
        public void CleanTitle_RemovesSeriesNameAndFolgePrefix()
        {
            // "Kira Kolumna - Folge 26 - Zusammengewachsen" → "Zusammengewachsen"
            NewEpisodeCardViewModel card = BuildCard(
                isAnnounced: false, releaseDate: DateTime.UtcNow,
                seriesName: "Kira Kolumna",
                episodeTitle: "Kira Kolumna - Folge 26 - Zusammengewachsen");

            Assert.Equal("Zusammengewachsen", card.EpisodeTitle);
        }

        [Fact]
        public void CleanTitle_RemovesSeriesNameAndNumberWithColon()
        {
            // "Fünf Freunde - Folge 170: und das Flüstern" → "und das Flüstern"
            NewEpisodeCardViewModel card = BuildCard(
                isAnnounced: false, releaseDate: DateTime.UtcNow,
                seriesName: "Fünf Freunde",
                episodeTitle: "Fünf Freunde - Folge 170: und das Flüstern");

            Assert.Equal("und das Flüstern", card.EpisodeTitle);
        }

        [Fact]
        public void CleanTitle_RemovesNumberDashPrefix()
        {
            // "TKKG - 218 - Der Goldschatz" → "Der Goldschatz"
            NewEpisodeCardViewModel card = BuildCard(
                isAnnounced: false, releaseDate: DateTime.UtcNow,
                seriesName: "TKKG",
                episodeTitle: "TKKG - 218 - Der Goldschatz");

            Assert.Equal("Der Goldschatz", card.EpisodeTitle);
        }

        [Fact]
        public void CleanTitle_KeepsOriginalWhenNoMatch()
        {
            // Kein Serienname-Prefix → Titel bleibt
            NewEpisodeCardViewModel card = BuildCard(
                isAnnounced: false, releaseDate: DateTime.UtcNow,
                seriesName: "Andere Serie",
                episodeTitle: "Ein ganz anderer Titel");

            Assert.Equal("Ein ganz anderer Titel", card.EpisodeTitle);
        }

        [Fact]
        public void CleanTitle_KeepsOriginalWhenOnlyPrefixExists()
        {
            // Nur Serienname ohne Titel dahinter → Original beibehalten
            NewEpisodeCardViewModel card = BuildCard(
                isAnnounced: false, releaseDate: DateTime.UtcNow,
                seriesName: "TKKG",
                episodeTitle: "TKKG");

            Assert.Equal("TKKG", card.EpisodeTitle);
        }

        // ── InfoLineVisibility / ReleaseDateVisibility ────────────────────

        [Fact]
        public void InfoLineVisibility_WithReleaseDate_Visible()
        {
            NewEpisodeCardViewModel card = BuildCard(
                isAnnounced: false, releaseDate: DateTime.UtcNow.AddDays(-3), episodeNumber: 170);

            Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, card.InfoLineVisibility);
        }

        [Fact]
        public void InfoLineVisibility_WithoutReleaseDate_Collapsed()
        {
            NewEpisodeCardViewModel card = BuildCard(
                isAnnounced: false, releaseDate: null);

            Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, card.InfoLineVisibility);
        }

        [Fact]
        public void ReleaseDateVisibility_Announced_Visible()
        {
            NewEpisodeCardViewModel card = BuildCard(
                isAnnounced: true, releaseDate: DateTime.UtcNow.AddDays(14));

            Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, card.ReleaseDateVisibility);
        }

        [Fact]
        public void ReleaseDateVisibility_PastDate_Collapsed()
        {
            NewEpisodeCardViewModel card = BuildCard(
                isAnnounced: false, releaseDate: DateTime.UtcNow.AddDays(-5));

            Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, card.ReleaseDateVisibility);
        }

        [Fact]
        public void InfoLine_Announced_ContainsAnnouncedLabel()
        {
            // Angekündigte Episode: "Nr. 26 · dd.MM.yyyy · angekündigt"
            DateTime future = DateTime.UtcNow.AddDays(14);
            NewEpisodeCardViewModel card = BuildCard(
                isAnnounced: true, releaseDate: future, episodeNumber: 26);

            Assert.Contains("Nr. 26", card.InfoLineText!);
            Assert.Contains("angekündigt", card.InfoLineText!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void InfoLine_NewRelease_ContainsOnlineLabel()
        {
            // Neuerscheinung ohne lokalen Track: "Nr. 170 · dd.MM.yyyy · online"
            NewEpisodeCardViewModel card = BuildCard(
                isAnnounced: false, releaseDate: DateTime.UtcNow.AddDays(-3), episodeNumber: 170);

            Assert.Contains("Nr. 170", card.InfoLineText!);
            Assert.Contains("online", card.InfoLineText!);
        }
    }
}
