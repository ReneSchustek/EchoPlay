using EchoPlay.Core.Abstractions.Time;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using EchoPlay.Spotify.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="ImportService"/>.
    /// Prüft Suche, Duplikaterkennung und den vollständigen Importablauf.
    /// Keyed-Services werden mit je einem Fake pro Provider registriert.
    /// </summary>
    public sealed class ImportServiceTests
    {
        /// <summary>Einstellungen für <see cref="BuildService"/>: die Nachbauten, aus denen der Prüfling gebaut wird.</summary>
        private sealed record ImportSetup
        {
            public required FakeAppSettingsDataService SettingsService { get; init; }
            public required FakeSeriesDataService SeriesService { get; init; }
            public required FakeEpisodeDataService EpisodeService { get; init; }
            public required FakeSeriesImportSearch SpotifySearch { get; init; }
            public required FakeSeriesImportSearch AppleMusicSearch { get; init; }
            public required FakeEpisodeImportSource SpotifyEpisodeSource { get; init; }
            public required FakeEpisodeImportSource AppleMusicEpisodeSource { get; init; }
            public FakeSpotifyClientCredentialsProvider? CredentialsProvider { get; init; }
        }

        /// <summary>
        /// Baut einen ServiceProvider mit Fakes und gibt einen fertigen ImportService zurück.
        /// </summary>
        private static ImportService BuildService(ImportSetup setup)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => setup.SettingsService);
            _ = services.AddScoped<ISeriesDataService>(_ => setup.SeriesService);
            _ = services.AddScoped<IWatchedTitleDataService>(_ => new FakeWatchedTitleDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => setup.EpisodeService);

            // Keyed-Registrierung, entspricht dem Produktionscode in App.xaml.cs
            _ = services.AddKeyedScoped<ISeriesImportSearch>("Spotify", (_, _) => setup.SpotifySearch);
            _ = services.AddKeyedScoped<ISeriesImportSearch>("AppleMusic", (_, _) => setup.AppleMusicSearch);
            _ = services.AddKeyedScoped<IEpisodeImportSource>("Spotify", (_, _) => setup.SpotifyEpisodeSource);
            _ = services.AddKeyedScoped<IEpisodeImportSource>("AppleMusic", (_, _) => setup.AppleMusicEpisodeSource);

            // ImportService prüft vor dem Spotify-Suchlauf, ob Credentials hinterlegt sind.
            // Default für bestehende Tests: Credentials vorhanden, die bestehende Erwartungshaltung bleibt.
            _ = services.AddSingleton<ISpotifyClientCredentialsProvider>(
                setup.CredentialsProvider ?? FakeSpotifyClientCredentialsProvider.WithCredentials());

            _ = services.AddSingleton<ILoggerFactory>(new FakeLoggerFactory());
            _ = services.AddSingleton<IClock>(new FakeClock());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddHttpClient();
            _ = services.AddSingleton<CoverService>();
            _ = services.AddSingleton<ICoverService>(sp => sp.GetRequiredService<CoverService>());
            _ = services.AddSingleton<ICoverDownloader>(new FakeCoverDownloader());
            _ = services.AddSingleton<EpisodeCoverCacheService>();
            ServiceProvider provider = services.BuildServiceProvider();
            return new ImportService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<EpisodeCoverCacheService>(),
                provider.GetRequiredService<ILoggerFactory>());
        }

        /// <summary>
        /// Importdienst, bei dem nur Apple Music Folgen liefert; Suche und Spotify bleiben leer.
        /// </summary>
        private static ImportService BuildAppleMusicService(
            FakeSeriesDataService seriesService,
            FakeEpisodeDataService episodeService,
            IReadOnlyList<ImportEpisode> appleMusicEpisodes)
            => BuildService(new()
            {
                SettingsService = new FakeAppSettingsDataService(new AppSettings()),
                SeriesService = seriesService,
                EpisodeService = episodeService,
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource(appleMusicEpisodes),
            });

        /// <summary>Legt die Serie an und liefert sie zurück, wie der Datendienst sie hält.</summary>
        private static async Task<Series> AddSeriesAsync(FakeSeriesDataService seriesService, Series series)
        {
            await seriesService.AddAsync(series, cancellationToken: TestContext.Current.CancellationToken);
            return seriesService.All[^1];
        }

        /// <summary>Legt „Folge n“ der Serie an, wahlweise mit Cover oder Apple-Music-Album.</summary>
        private static Task AddEpisodeAsync(
            FakeEpisodeDataService episodeService,
            Series series,
            int number,
            string? coverUrl = null,
            string? appleMusicAlbumId = null)
            => episodeService.AddAsync(
                new Episode
                {
                    SeriesId = series.Id,
                    Title = $"Folge {number}",
                    EpisodeNumber = number,
                    CoverImageUrl = coverUrl,
                    AppleMusicAlbumId = appleMusicAlbumId
                },
                cancellationToken: TestContext.Current.CancellationToken);

        /// <summary>„Folge n“, wie der Anbieter sie liefert, wahlweise mit Cover-Adresse.</summary>
        private static ImportEpisode ProviderEpisode(string sourceId, int number, string? coverUrl = null)
            => new()
            {
                SourceEpisodeId = sourceId,
                Title = $"Folge {number}",
                EpisodeNumber = number,
                CoverImageUrl = coverUrl is null ? null : new Uri(coverUrl)
            };

        /// <summary>Ein Hörspiel-Künstler aus der Apple-Music-Suche.</summary>
        private static ImportSeries AppleMusicArtist(string sourceId, string title)
            => new()
            {
                SourceSeriesId = sourceId,
                Source = "AppleMusic",
                Title = title,
                IsHoerspiel = true,
                Score = 80
            };

        [Fact]
        public async Task SearchAsync_ReturnsSpotifyResults_WhenProviderIsSpotify()
        {
            // Spotify-Provider liefert seine Ergebnisse, wenn ActiveProvider = Spotify
            IReadOnlyList<ImportSeries> spotifyResults =
            [
                new() { SourceSeriesId = "sp1", Source = "Spotify", Title = "Die drei ???", IsHoerspiel = true, Score = 80 }
            ];

            IReadOnlyList<ImportSeries> appleMusicResults =
            [
                new() { SourceSeriesId = "am1", Source = "AppleMusic", Title = "TKKG", IsHoerspiel = true, Score = 70 }
            ];

            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.Spotify });
            FakeSeriesDataService series = new();
            FakeEpisodeDataService episodes = new();
            ImportService service = BuildService(new()
            {
                SettingsService = settings,
                SeriesService = series,
                EpisodeService = episodes,
                SpotifySearch = new FakeSeriesImportSearch(spotifyResults, "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch(appleMusicResults, "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
            });

            SearchOutcome result = await service.SearchAsync("drei", cancellationToken: TestContext.Current.CancellationToken);

            _ = Assert.Single(result.Results);
            Assert.Equal("Spotify", result.Results[0].Source);
            Assert.False(result.SpotifyFallbackApplied);
        }

        [Fact]
        public async Task SearchAsync_ReturnsAppleMusicResults_WhenProviderIsAppleMusic()
        {
            // AppleMusic-Provider liefert seine Ergebnisse, wenn ActiveProvider = AppleMusic
            IReadOnlyList<ImportSeries> spotifyResults =
            [
                new() { SourceSeriesId = "sp1", Source = "Spotify", Title = "Die drei ???", IsHoerspiel = true, Score = 80 }
            ];

            IReadOnlyList<ImportSeries> appleMusicResults =
            [
                new() { SourceSeriesId = "am1", Source = "AppleMusic", Title = "TKKG", IsHoerspiel = true, Score = 70 }
            ];

            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.AppleMusic });
            FakeSeriesDataService series = new();
            FakeEpisodeDataService episodes = new();
            ImportService service = BuildService(new()
            {
                SettingsService = settings,
                SeriesService = series,
                EpisodeService = episodes,
                SpotifySearch = new FakeSeriesImportSearch(spotifyResults, "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch(appleMusicResults, "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
            });

            SearchOutcome result = await service.SearchAsync("tkkg", cancellationToken: TestContext.Current.CancellationToken);

            _ = Assert.Single(result.Results);
            Assert.Equal("AppleMusic", result.Results[0].Source);
            Assert.False(result.SpotifyFallbackApplied);
        }

        [Fact]
        public async Task SearchAsync_ReturnsEmpty_WhenNoResults()
        {
            // Leere Ergebnisliste wird korrekt weitergegeben
            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.Spotify });
            FakeSeriesDataService series = new();
            FakeEpisodeDataService episodes = new();
            ImportService service = BuildService(new()
            {
                SettingsService = settings,
                SeriesService = series,
                EpisodeService = episodes,
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
            });

            SearchOutcome result = await service.SearchAsync("unbekannt", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(result.Results);
            Assert.False(result.SpotifyFallbackApplied);
        }

        [Fact]
        public async Task SearchAsync_FallsBackToAppleMusic_WhenSpotifyActiveWithoutCredentials()
        {
            // Nutzer hat Spotify als aktiven Provider gewählt, aber keine Credentials hinterlegt.
            // Erwartung: Suche nutzt Apple Music, Flag SpotifyFallbackApplied = true.
            IReadOnlyList<ImportSeries> spotifyResults =
            [
                new() { SourceSeriesId = "sp1", Source = "Spotify", Title = "Spotify-Treffer", IsHoerspiel = true, Score = 80 }
            ];
            IReadOnlyList<ImportSeries> appleMusicResults =
            [
                new() { SourceSeriesId = "am1", Source = "AppleMusic", Title = "Apple-Treffer", IsHoerspiel = true, Score = 70 }
            ];

            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.Spotify });
            FakeSeriesDataService series = new();
            FakeEpisodeDataService episodes = new();
            ImportService service = BuildService(new()
            {
                SettingsService = settings,
                SeriesService = series,
                EpisodeService = episodes,
                SpotifySearch = new FakeSeriesImportSearch(spotifyResults, "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch(appleMusicResults, "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
                CredentialsProvider = FakeSpotifyClientCredentialsProvider.Missing(),
            });

            SearchOutcome result = await service.SearchAsync("query", cancellationToken: TestContext.Current.CancellationToken);

            _ = Assert.Single(result.Results);
            Assert.Equal("AppleMusic", result.Results[0].Source);
            Assert.True(result.SpotifyFallbackApplied);
        }

        [Fact]
        public async Task SearchAsync_NoFallback_WhenAppleMusicActiveAndCredentialsMissing()
        {
            // AppleMusic aktiv: Fehlende Spotify-Credentials sind irrelevant, kein Fallback-Signal.
            IReadOnlyList<ImportSeries> appleMusicResults =
            [
                new() { SourceSeriesId = "am1", Source = "AppleMusic", Title = "Apple-Treffer", IsHoerspiel = true, Score = 70 }
            ];

            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.AppleMusic });
            FakeSeriesDataService series = new();
            FakeEpisodeDataService episodes = new();
            ImportService service = BuildService(new()
            {
                SettingsService = settings,
                SeriesService = series,
                EpisodeService = episodes,
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch(appleMusicResults, "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
                CredentialsProvider = FakeSpotifyClientCredentialsProvider.Missing(),
            });

            SearchOutcome result = await service.SearchAsync("query", cancellationToken: TestContext.Current.CancellationToken);

            _ = Assert.Single(result.Results);
            Assert.Equal("AppleMusic", result.Results[0].Source);
            Assert.False(result.SpotifyFallbackApplied);
        }

        [Fact]
        public async Task SearchAsync_NoFallback_WhenProviderIsNone()
        {
            // Kein aktiver Provider: leeres Ergebnis, kein Fallback-Signal.
            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.None });
            FakeSeriesDataService series = new();
            FakeEpisodeDataService episodes = new();
            ImportService service = BuildService(new()
            {
                SettingsService = settings,
                SeriesService = series,
                EpisodeService = episodes,
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
                CredentialsProvider = FakeSpotifyClientCredentialsProvider.Missing(),
            });

            SearchOutcome result = await service.SearchAsync("query", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(result.Results);
            Assert.False(result.SpotifyFallbackApplied);
        }

        [Fact]
        public async Task SearchAlbumsAsync_SetsFallbackFlag_WhenSpotifyActiveWithoutCredentials()
        {
            // Alben-Suche folgt derselben Fallback-Logik. Ohne registrierten AppleMusic-Client
            // bleibt die Trefferliste leer, aber das Flag muss gesetzt sein.
            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.Spotify });
            FakeSeriesDataService series = new();
            FakeEpisodeDataService episodes = new();
            ImportService service = BuildService(new()
            {
                SettingsService = settings,
                SeriesService = series,
                EpisodeService = episodes,
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
                CredentialsProvider = FakeSpotifyClientCredentialsProvider.Missing(),
            });

            SearchOutcome result = await service.SearchAlbumsAsync("query", cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.SpotifyFallbackApplied);
        }

        [Fact]
        public async Task SearchAlbumsAsync_NoFallback_WhenSpotifyActiveWithCredentials()
        {
            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.Spotify });
            FakeSeriesDataService series = new();
            FakeEpisodeDataService episodes = new();
            ImportService service = BuildService(new()
            {
                SettingsService = settings,
                SeriesService = series,
                EpisodeService = episodes,
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
            });

            SearchOutcome result = await service.SearchAlbumsAsync("query", cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result.SpotifyFallbackApplied);
        }

        [Fact]
        public async Task SearchAlbumsAsync_NoFallback_WhenQueryIsEmpty()
        {
            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.Spotify });
            FakeSeriesDataService series = new();
            FakeEpisodeDataService episodes = new();
            ImportService service = BuildService(new()
            {
                SettingsService = settings,
                SeriesService = series,
                EpisodeService = episodes,
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
                CredentialsProvider = FakeSpotifyClientCredentialsProvider.Missing(),
            });

            SearchOutcome result = await service.SearchAlbumsAsync("   ", cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(result.Results);
            Assert.False(result.SpotifyFallbackApplied);
        }

        [Fact]
        public async Task ImportAsync_CreatesSeries_WithCorrectTitle()
        {
            // Nach dem Import muss die Serie mit korrektem Titel in der DB vorliegen
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakeAppSettingsDataService settings = new(new AppSettings());
            ImportService service = BuildService(new()
            {
                SettingsService = settings,
                SeriesService = seriesService,
                EpisodeService = episodeService,
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
            });

            ImportSeries importSeries = new()
            {
                SourceSeriesId = "artist-123",
                Source = "AppleMusic",
                Title = "Die drei ???",
                IsHoerspiel = true,
                Score = 80
            };

            _ = await service.ImportAsync(importSeries, cancellationToken: TestContext.Current.CancellationToken);

            _ = Assert.Single(seriesService.All);
            Assert.Equal("Die drei ???", seriesService.All[0].Title);
        }

        [Fact]
        public async Task ImportAsync_CreatesAllEpisodes()
        {
            // Alle importierten Episoden müssen in der DB angelegt werden
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakeAppSettingsDataService settings = new(new AppSettings());

            IReadOnlyList<ImportEpisode> importEpisodes =
            [
                new() { SourceEpisodeId = "ep1", Title = "Folge 1", EpisodeNumber = 1 },
                new() { SourceEpisodeId = "ep2", Title = "Folge 2", EpisodeNumber = 2 },
                new() { SourceEpisodeId = "ep3", Title = "Folge 3", EpisodeNumber = 3 },
            ];

            ImportService service = BuildService(new()
            {
                SettingsService = settings,
                SeriesService = seriesService,
                EpisodeService = episodeService,
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource(importEpisodes),
            });

            ImportSeries importSeries = new()
            {
                SourceSeriesId = "artist-123",
                Source = "AppleMusic",
                Title = "TKKG",
                IsHoerspiel = true,
                Score = 90
            };

            _ = await service.ImportAsync(importSeries, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(3, episodeService.All.Count);
        }

        [Fact]
        public async Task ImportAsync_DeduplicatesEpisodes_BySourceEpisodeId()
        {
            // Provider liefern bei Compilation/Re-Release dieselbe Folge mehrfach.
            // Nach dem Import darf in der DB nur eine Episode pro SourceEpisodeId stehen.
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            IReadOnlyList<ImportEpisode> importEpisodesWithDuplicates =
            [
                new() { SourceEpisodeId = "album-1", Title = "Folge 1: Auftakt", EpisodeNumber = 1 },
                new() { SourceEpisodeId = "album-2", Title = "Folge 2: Spuren", EpisodeNumber = 2 },
                new() { SourceEpisodeId = "album-1", Title = "Folge 1: Auftakt (Re-Release)", EpisodeNumber = 1 },
                new() { SourceEpisodeId = "album-3", Title = "Folge 3: Verfolgung", EpisodeNumber = 3 },
                new() { SourceEpisodeId = "album-2", Title = "Folge 2: Spuren (Compilation)", EpisodeNumber = 2 },
            ];

            ImportService service = BuildAppleMusicService(seriesService, episodeService, importEpisodesWithDuplicates);

            _ = await service.ImportAsync(
                AppleMusicArtist("artist-scotland-yard", "Scotland Yard"),
                cancellationToken: TestContext.Current.CancellationToken);

            // Drei eindeutige SourceEpisodeIds -> exakt drei Episoden, das erste Vorkommen gewinnt.
            Assert.Equal(3, episodeService.All.Count);
            Assert.Equal("Folge 1: Auftakt", episodeService.All[0].Title);
            Assert.Equal("Folge 2: Spuren", episodeService.All[1].Title);
            Assert.Equal("Folge 3: Verfolgung", episodeService.All[2].Title);
        }

        [Fact]
        public async Task ImportAsync_PreservesAllEpisodes_WhenNoDuplicates()
        {
            // Konsistenzprüfung: ohne Duplikate darf der Dedup-Filter nichts entfernen.
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakeAppSettingsDataService settings = new(new AppSettings());

            IReadOnlyList<ImportEpisode> importEpisodes =
            [
                new() { SourceEpisodeId = "album-1", Title = "Folge 1", EpisodeNumber = 1 },
                new() { SourceEpisodeId = "album-2", Title = "Folge 2", EpisodeNumber = 2 },
                new() { SourceEpisodeId = "album-3", Title = "Folge 3", EpisodeNumber = 3 },
            ];

            ImportService service = BuildService(new()
            {
                SettingsService = settings,
                SeriesService = seriesService,
                EpisodeService = episodeService,
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource(importEpisodes),
            });

            ImportSeries importSeries = new()
            {
                SourceSeriesId = "artist-clean",
                Source = "AppleMusic",
                Title = "Saubere Serie",
                IsHoerspiel = true,
                Score = 70
            };

            _ = await service.ImportAsync(importSeries, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(3, episodeService.All.Count);
        }

        [Fact]
        public async Task ImportAsync_SkipsImport_WhenAlreadyImported()
        {
            // Bereits vorhandene Serie darf nicht doppelt angelegt werden
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakeAppSettingsDataService settings = new(new AppSettings());

            await seriesService.AddAsync(new Series
            {
                Title = "Bestehende Serie",
                AppleMusicArtistId = "artist-existing",
                IsOnlineImported = true
            }, cancellationToken: TestContext.Current.CancellationToken);

            ImportService service = BuildService(new()
            {
                SettingsService = settings,
                SeriesService = seriesService,
                EpisodeService = episodeService,
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
            });

            ImportSeries importSeries = new()
            {
                SourceSeriesId = "artist-existing",
                Source = "AppleMusic",
                Title = "Bestehende Serie",
                IsHoerspiel = true,
                Score = 70
            };

            _ = await service.ImportAsync(importSeries, cancellationToken: TestContext.Current.CancellationToken);

            // Nur die initial angelegte Serie darf vorhanden sein
            _ = Assert.Single(seriesService.All);
        }

        [Fact]
        public async Task ImportAsync_ReturnsNewSeriesId()
        {
            // Der Rückgabewert muss eine gültige GUID sein
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakeAppSettingsDataService settings = new(new AppSettings());
            ImportService service = BuildService(new()
            {
                SettingsService = settings,
                SeriesService = seriesService,
                EpisodeService = episodeService,
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
            });

            ImportSeries importSeries = new()
            {
                SourceSeriesId = "artist-new",
                Source = "AppleMusic",
                Title = "Neue Serie",
                IsHoerspiel = true,
                Score = 60
            };

            Guid id = await service.ImportAsync(importSeries, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEqual(Guid.Empty, id);
        }

        [Fact]
        public async Task IsAlreadyImportedAsync_ReturnsTrue_WhenSeriesExistsBySpotifyId()
        {
            // Wenn eine Serie mit der Spotify-ID bereits existiert, muss True zurückgegeben werden
            FakeSeriesDataService seriesService = new();
            FakeAppSettingsDataService settings = new(new AppSettings());
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series
            {
                Title = "Vorhandene Serie",
                SpotifyArtistId = "spotify-123",
                IsOnlineImported = true
            }, cancellationToken: TestContext.Current.CancellationToken);

            ImportService service = BuildService(new()
            {
                SettingsService = settings,
                SeriesService = seriesService,
                EpisodeService = episodeService,
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
            });

            ImportSeries series = new()
            {
                SourceSeriesId = "spotify-123",
                Source = "Spotify",
                Title = "Vorhandene Serie",
                IsHoerspiel = true,
                Score = 75
            };

            bool result = await service.IsAlreadyImportedAsync(series, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result);
        }

        [Fact]
        public async Task ReImportEpisodesAsync_PersistsAllEpisodes_ViaSingleAddRangeCall()
        {
            // Re-Import muss einen Batch-Insert auslösen statt N einzelne AddAsync-Aufrufe (N+1 vermeiden).
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakeAppSettingsDataService settings = new(new AppSettings());

            await seriesService.AddAsync(new Series
            {
                Title = "TKKG",
                AppleMusicArtistId = "artist-tkkg"
            }, cancellationToken: TestContext.Current.CancellationToken);

            Series existingSeries = seriesService.All[0];

            IReadOnlyList<ImportEpisode> providerEpisodes =
            [
                new() { SourceEpisodeId = "ep1", Title = "Folge 1", EpisodeNumber = 1 },
                new() { SourceEpisodeId = "ep2", Title = "Folge 2", EpisodeNumber = 2 },
                new() { SourceEpisodeId = "ep3", Title = "Folge 3", EpisodeNumber = 3 },
            ];

            ImportService service = BuildService(new()
            {
                SettingsService = settings,
                SeriesService = seriesService,
                EpisodeService = episodeService,
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource(providerEpisodes),
            });

            int count = await service.ReImportEpisodesAsync(existingSeries, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(3, count);
            Assert.Equal(3, episodeService.All.Count);
            Assert.Equal(1, episodeService.AddRangeAsyncCallCount);
            Assert.Equal(0, episodeService.AddAsyncCallCount);
        }

        [Fact]
        public async Task DeltaImportEpisodesAsync_AddsOnlyNewEpisodes_ViaSingleAddRangeCall()
        {
            // Delta-Import muss neue Episoden sammeln und in einem einzigen AddRangeAsync persistieren.
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            Series existingSeries = await AddSeriesAsync(
                seriesService, new Series { Title = "TKKG", AppleMusicArtistId = "artist-tkkg" });

            // Drei bestehende Episoden; der Titel-Lookup wird sie ausfiltern.
            await AddEpisodeAsync(episodeService, existingSeries, 1);
            await AddEpisodeAsync(episodeService, existingSeries, 2);
            await AddEpisodeAsync(episodeService, existingSeries, 3);

            // Provider liefert dieselben drei Folgen + zwei neue.
            ImportService service = BuildAppleMusicService(seriesService, episodeService,
            [
                ProviderEpisode("ep1", 1), ProviderEpisode("ep2", 2), ProviderEpisode("ep3", 3),
                ProviderEpisode("ep4", 4), ProviderEpisode("ep5", 5),
            ]);

            int newCount = await service.DeltaImportEpisodesAsync(existingSeries, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, newCount);
            Assert.Equal(5, episodeService.All.Count);
            // Nur ein einziger Batch-Insert für die zwei neuen Folgen.
            Assert.Equal(1, episodeService.AddRangeAsyncCallCount);
            // 3 vorhandene Episoden hatten kein Cover-Update, also kein UpdateRangeAsync.
            Assert.Equal(0, episodeService.UpdateRangeAsyncCallCount);
        }

        [Fact]
        public async Task DeltaImportEpisodesAsync_UpdatesExistingCoverInBatch()
        {
            // Bestehende Episoden ohne Cover sollen die Provider-CoverUrl in einem einzigen
            // UpdateRangeAsync-Aufruf bekommen, nicht pro Treffer (Batch statt N+1).
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            Series existingSeries = await AddSeriesAsync(
                seriesService, new Series { Title = "TKKG", AppleMusicArtistId = "artist-tkkg" });

            // Zwei vorhandene Folgen ohne Cover, eine mit bereits gesetztem Cover.
            await AddEpisodeAsync(episodeService, existingSeries, 1);
            await AddEpisodeAsync(episodeService, existingSeries, 2);
            await AddEpisodeAsync(episodeService, existingSeries, 3, coverUrl: "https://example.org/already.jpg");

            ImportService service = BuildAppleMusicService(seriesService, episodeService,
            [
                ProviderEpisode("ep1", 1, "https://example.org/cover1.jpg"),
                ProviderEpisode("ep2", 2, "https://example.org/cover2.jpg"),
                ProviderEpisode("ep3", 3, "https://example.org/different.jpg"),
            ]);

            int newCount = await service.DeltaImportEpisodesAsync(existingSeries, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(0, newCount);
            // Ein einziger Batch-Update für die zwei nachzutragenden Cover.
            Assert.Equal(1, episodeService.UpdateRangeAsyncCallCount);
            Assert.Equal(0, episodeService.UpdateAsyncCallCount);
            Assert.Equal(0, episodeService.AddRangeAsyncCallCount);
            Assert.Equal("https://example.org/cover1.jpg", episodeService.All[0].CoverImageUrl);
            Assert.Equal("https://example.org/cover2.jpg", episodeService.All[1].CoverImageUrl);
            // Bestehendes Cover darf nicht überschrieben werden.
            Assert.Equal("https://example.org/already.jpg", episodeService.All[2].CoverImageUrl);
        }

        [Fact]
        public async Task DeltaImportEpisodesAsync_FallsBackToAppleMusic_WhenSpotifyCredentialsMissing()
        {
            // Serie mit Spotify- UND Apple-ID. Ohne hinterlegte Spotify-Credentials darf der
            // Delta-Import nicht stillschweigend über den unbrauchbaren Spotify-Provider laufen,
            // sondern muss auf Apple Music ausweichen, sonst kämen neue Folgen nie an
            // (real beobachtet: Fünf Freunde blieb bei Folge 169, obwohl Apple 170/171 führt).
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakeAppSettingsDataService settings = new(new AppSettings());

            await seriesService.AddAsync(new Series
            {
                Title = "Fünf Freunde",
                SpotifyArtistId = "spotify-artist",
                AppleMusicArtistId = "216347875"
            }, cancellationToken: TestContext.Current.CancellationToken);
            Series existingSeries = seriesService.All[0];

            await episodeService.AddAsync(new Episode { SeriesId = existingSeries.Id, Title = "Folge 169", EpisodeNumber = 169 }, cancellationToken: TestContext.Current.CancellationToken);

            // Spotify-Quelle liefert (unbenutzt) nichts; nur Apple hat die neue Folge 170.
            FakeEpisodeImportSource spotifySource = new([]);
            FakeEpisodeImportSource appleSource = new(
            [
                new() { SourceEpisodeId = "am169", Title = "Folge 169", EpisodeNumber = 169 },
                new() { SourceEpisodeId = "am170", Title = "Folge 170", EpisodeNumber = 170 },
            ]);

            ImportService service = BuildService(new()
            {
                SettingsService = settings,
                SeriesService = seriesService,
                EpisodeService = episodeService,
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = spotifySource,
                AppleMusicEpisodeSource = appleSource,
                CredentialsProvider = FakeSpotifyClientCredentialsProvider.Missing(),
            });

            int newCount = await service.DeltaImportEpisodesAsync(existingSeries, cancellationToken: TestContext.Current.CancellationToken);

            // Genau die neue Folge 170 muss über Apple Music angekommen sein.
            Assert.Equal(1, newCount);
            Assert.Contains(episodeService.All, e => e.Title == "Folge 170");
        }

        [Fact]
        public async Task DeltaImportEpisodesAsync_UsesSpotify_WhenCredentialsPresent()
        {
            // Gegenprobe: Sind Spotify-Credentials vorhanden, bleibt Spotify der bevorzugte Provider
            // für eine Serie mit beiden IDs; der Apple-Fallback darf dann NICHT greifen.
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakeAppSettingsDataService settings = new(new AppSettings());

            await seriesService.AddAsync(new Series
            {
                Title = "Fünf Freunde",
                SpotifyArtistId = "spotify-artist",
                AppleMusicArtistId = "216347875"
            }, cancellationToken: TestContext.Current.CancellationToken);
            Series existingSeries = seriesService.All[0];

            await episodeService.AddAsync(new Episode { SeriesId = existingSeries.Id, Title = "Folge 169", EpisodeNumber = 169 }, cancellationToken: TestContext.Current.CancellationToken);

            // Nur die Spotify-Quelle hat die neue Folge; Apple liefert nichts.
            FakeEpisodeImportSource spotifySource = new(
            [
                new() { SourceEpisodeId = "sp170", Title = "Folge 170", EpisodeNumber = 170 },
            ]);
            FakeEpisodeImportSource appleSource = new([]);

            ImportService service = BuildService(new()
            {
                SettingsService = settings,
                SeriesService = seriesService,
                EpisodeService = episodeService,
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = spotifySource,
                AppleMusicEpisodeSource = appleSource,
                CredentialsProvider = FakeSpotifyClientCredentialsProvider.WithCredentials(),
            });

            int newCount = await service.DeltaImportEpisodesAsync(existingSeries, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, newCount);
            Assert.Contains(episodeService.All, e => e.Title == "Folge 170");
        }

        [Fact]
        public async Task IsAlreadyImportedAsync_ReturnsFalse_WhenSeriesNotFound()
        {
            // Unbekannte SourceSeriesId darf nicht als "bereits vorhanden" erkannt werden
            FakeSeriesDataService seriesService = new();
            FakeAppSettingsDataService settings = new(new AppSettings());
            FakeEpisodeDataService episodeService = new();
            ImportService service = BuildService(new()
            {
                SettingsService = settings,
                SeriesService = seriesService,
                EpisodeService = episodeService,
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
            });

            ImportSeries series = new()
            {
                SourceSeriesId = "unbekannt-999",
                Source = "Spotify",
                Title = "Nicht vorhanden",
                IsHoerspiel = true,
                Score = 40
            };

            bool result = await service.IsAlreadyImportedAsync(series, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
        }

        [Fact]
        public async Task IsAlreadyImportedAsync_ReturnsFalse_WhenOnlyLocalSeriesCarriesTheArtistId()
        {
            // Eine lokal eingelesene Serie bekommt ihre Apple-Music-Artist-ID von der
            // Neuerscheinungs-Prüfung. Zählte sie als „bereits importiert", blendete die Suche
            // den Hinzufügen-Knopf aus und der Künstler ließe sich nie in die Online-Mediathek
            // holen (real beobachtet an „Die Playmos").
            FakeSeriesDataService seriesService = new();
            FakeAppSettingsDataService settings = new(new AppSettings());
            FakeEpisodeDataService episodeService = new();

            await seriesService.AddAsync(new Series
            {
                Title = "Die Playmos",
                AppleMusicArtistId = "267092844",
                LocalFolderPath = @"D:\Hörspiele\Die Playmos",
                IsOnlineImported = false
            }, cancellationToken: TestContext.Current.CancellationToken);

            ImportService service = BuildService(new()
            {
                SettingsService = settings,
                SeriesService = seriesService,
                EpisodeService = episodeService,
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource([]),
            });

            ImportSeries series = new()
            {
                SourceSeriesId = "267092844",
                Source = "AppleMusic",
                Title = "Die Playmos",
                IsHoerspiel = true,
                Score = 80
            };

            bool result = await service.IsAlreadyImportedAsync(series, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
        }

        [Fact]
        public async Task ImportAsync_ImportsArtist_WhenOnlyLocalSeriesCarriesTheArtistId()
        {
            // Gegenprobe zum vorigen Test auf dem Schreibpfad: Der Künstler muss wirklich in die
            // Online-Mediathek gelangen und darf nicht am Vorhanden-Check abprallen.
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();

            _ = await AddSeriesAsync(seriesService, new Series
            {
                Title = "Die Playmos",
                AppleMusicArtistId = "267092844",
                LocalFolderPath = @"D:\Hörspiele\Die Playmos",
                IsOnlineImported = false
            });

            ImportService service = BuildAppleMusicService(seriesService, episodeService, [ProviderEpisode("am1", 1)]);

            _ = await service.ImportAsync(
                AppleMusicArtist("267092844", "Die Playmos"), cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, seriesService.All.Count);
            Assert.Contains(seriesService.All, s => s.IsOnlineImported && s.AppleMusicArtistId == "267092844");
        }

        [Fact]
        public async Task ReImportEpisodesAsync_SkipsEpisodesAlreadyInTheLibrary()
        {
            // Der Re-Import läuft an, während der Erstimport seine Folgen schon geschrieben hat,
            // genau die Verschränkung, die den Bestand einmal verdoppelt hat. Er darf nur die
            // fehlende Folge anlegen.
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            Series existingSeries = await AddSeriesAsync(seriesService, new Series
            {
                Title = "Die drei ???",
                AppleMusicArtistId = "201306317",
                IsOnlineImported = true
            });

            await AddEpisodeAsync(episodeService, existingSeries, 229, appleMusicAlbumId: "am229");
            await AddEpisodeAsync(episodeService, existingSeries, 230, appleMusicAlbumId: "am230");

            ImportService service = BuildAppleMusicService(seriesService, episodeService,
                [ProviderEpisode("am229", 229), ProviderEpisode("am230", 230), ProviderEpisode("am231", 231)]);

            int count = await service.ReImportEpisodesAsync(existingSeries, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, count);
            Assert.Equal(3, episodeService.All.Count);
            _ = Assert.Single(episodeService.All, e => e.Title == "Folge 230");
        }

        [Fact]
        public async Task DeltaImportEpisodesAsync_SkipsKnownAlbum_WhenProviderRenamedIt()
        {
            // Der Anbieter benennt Alben nachträglich um. Beim reinen Titelvergleich käme die
            // Folge als zweite Zeile in den Bestand; die Album-Kennung erkennt sie wieder.
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakeAppSettingsDataService settings = new(new AppSettings());

            await seriesService.AddAsync(new Series
            {
                Title = "TKKG",
                AppleMusicArtistId = "artist-tkkg",
                IsOnlineImported = true
            }, cancellationToken: TestContext.Current.CancellationToken);
            Series existingSeries = seriesService.All[0];

            await episodeService.AddAsync(new Episode
            {
                SeriesId = existingSeries.Id,
                Title = "Folge 240",
                EpisodeNumber = 240,
                AppleMusicAlbumId = "am240"
            }, cancellationToken: TestContext.Current.CancellationToken);

            ImportService service = BuildService(new()
            {
                SettingsService = settings,
                SeriesService = seriesService,
                EpisodeService = episodeService,
                SpotifySearch = new FakeSeriesImportSearch([], "Spotify"),
                AppleMusicSearch = new FakeSeriesImportSearch([], "AppleMusic"),
                SpotifyEpisodeSource = new FakeEpisodeImportSource([]),
                AppleMusicEpisodeSource = new FakeEpisodeImportSource(
                [
                    new() { SourceEpisodeId = "am240", Title = "Folge 240: Das verschollene Zepter", EpisodeNumber = 240 },
                ]),
            });

            int newCount = await service.DeltaImportEpisodesAsync(existingSeries, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(0, newCount);
            _ = Assert.Single(episodeService.All);
        }
    }
}
