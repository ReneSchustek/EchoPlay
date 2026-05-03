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
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="ImportService"/>.
    /// Prüft Suche, Duplikaterkennung und den vollständigen Importablauf.
    /// Keyed-Services werden mit je einem Fake pro Provider registriert.
    /// </summary>
    public sealed class ImportServiceTests
    {
        /// <summary>
        /// Baut einen ServiceProvider mit Fakes und gibt einen fertigen ImportService zurück.
        /// </summary>
        private static ImportService BuildService(
            FakeAppSettingsDataService settingsService,
            FakeSeriesDataService seriesService,
            FakeEpisodeDataService episodeService,
            FakeSeriesImportSearch spotifySearch,
            FakeSeriesImportSearch appleMusicSearch,
            FakeEpisodeImportSource spotifyEpisodeSource,
            FakeEpisodeImportSource appleMusicEpisodeSource,
            FakeSpotifyClientCredentialsProvider? credentialsProvider = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(_ => settingsService);
            _ = services.AddScoped<ISeriesDataService>(_ => seriesService);
            _ = services.AddScoped<IEpisodeDataService>(_ => episodeService);

            // Keyed-Registrierung – entspricht dem Produktionscode in App.xaml.cs
            _ = services.AddKeyedScoped<ISeriesImportSearch>("Spotify", (_, _) => spotifySearch);
            _ = services.AddKeyedScoped<ISeriesImportSearch>("AppleMusic", (_, _) => appleMusicSearch);
            _ = services.AddKeyedScoped<IEpisodeImportSource>("Spotify", (_, _) => spotifyEpisodeSource);
            _ = services.AddKeyedScoped<IEpisodeImportSource>("AppleMusic", (_, _) => appleMusicEpisodeSource);

            // ImportService prüft vor dem Spotify-Suchlauf, ob Credentials hinterlegt sind.
            // Default für bestehende Tests: Credentials vorhanden — bestehende Erwartungshaltung bleibt.
            _ = services.AddSingleton<ISpotifyClientCredentialsProvider>(
                credentialsProvider ?? FakeSpotifyClientCredentialsProvider.WithCredentials());

            _ = services.AddSingleton<ILoggerFactory>(new FakeLoggerFactory());
            _ = services.AddSingleton<IClock>(new FakeClock());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddHttpClient();
            _ = services.AddSingleton<CoverService>();
            _ = services.AddSingleton<EpisodeCoverCacheService>();
            ServiceProvider provider = services.BuildServiceProvider();
            return new ImportService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<EpisodeCoverCacheService>(),
                provider.GetRequiredService<ILoggerFactory>());
        }

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
            ImportService service = BuildService(settings, series, episodes,
                spotifySearch: new FakeSeriesImportSearch(spotifyResults, "Spotify"),
                appleMusicSearch: new FakeSeriesImportSearch(appleMusicResults, "AppleMusic"),
                spotifyEpisodeSource: new FakeEpisodeImportSource([]),
                appleMusicEpisodeSource: new FakeEpisodeImportSource([]));

            SearchOutcome result = await service.SearchAsync("drei");

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
            ImportService service = BuildService(settings, series, episodes,
                spotifySearch: new FakeSeriesImportSearch(spotifyResults, "Spotify"),
                appleMusicSearch: new FakeSeriesImportSearch(appleMusicResults, "AppleMusic"),
                spotifyEpisodeSource: new FakeEpisodeImportSource([]),
                appleMusicEpisodeSource: new FakeEpisodeImportSource([]));

            SearchOutcome result = await service.SearchAsync("tkkg");

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
            ImportService service = BuildService(settings, series, episodes,
                spotifySearch: new FakeSeriesImportSearch([], "Spotify"),
                appleMusicSearch: new FakeSeriesImportSearch([], "AppleMusic"),
                spotifyEpisodeSource: new FakeEpisodeImportSource([]),
                appleMusicEpisodeSource: new FakeEpisodeImportSource([]));

            SearchOutcome result = await service.SearchAsync("unbekannt");

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
            ImportService service = BuildService(settings, series, episodes,
                spotifySearch: new FakeSeriesImportSearch(spotifyResults, "Spotify"),
                appleMusicSearch: new FakeSeriesImportSearch(appleMusicResults, "AppleMusic"),
                spotifyEpisodeSource: new FakeEpisodeImportSource([]),
                appleMusicEpisodeSource: new FakeEpisodeImportSource([]),
                credentialsProvider: FakeSpotifyClientCredentialsProvider.Missing());

            SearchOutcome result = await service.SearchAsync("query");

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
            ImportService service = BuildService(settings, series, episodes,
                spotifySearch: new FakeSeriesImportSearch([], "Spotify"),
                appleMusicSearch: new FakeSeriesImportSearch(appleMusicResults, "AppleMusic"),
                spotifyEpisodeSource: new FakeEpisodeImportSource([]),
                appleMusicEpisodeSource: new FakeEpisodeImportSource([]),
                credentialsProvider: FakeSpotifyClientCredentialsProvider.Missing());

            SearchOutcome result = await service.SearchAsync("query");

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
            ImportService service = BuildService(settings, series, episodes,
                spotifySearch: new FakeSeriesImportSearch([], "Spotify"),
                appleMusicSearch: new FakeSeriesImportSearch([], "AppleMusic"),
                spotifyEpisodeSource: new FakeEpisodeImportSource([]),
                appleMusicEpisodeSource: new FakeEpisodeImportSource([]),
                credentialsProvider: FakeSpotifyClientCredentialsProvider.Missing());

            SearchOutcome result = await service.SearchAsync("query");

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
            ImportService service = BuildService(settings, series, episodes,
                spotifySearch: new FakeSeriesImportSearch([], "Spotify"),
                appleMusicSearch: new FakeSeriesImportSearch([], "AppleMusic"),
                spotifyEpisodeSource: new FakeEpisodeImportSource([]),
                appleMusicEpisodeSource: new FakeEpisodeImportSource([]),
                credentialsProvider: FakeSpotifyClientCredentialsProvider.Missing());

            SearchOutcome result = await service.SearchAlbumsAsync("query");

            Assert.True(result.SpotifyFallbackApplied);
        }

        [Fact]
        public async Task SearchAlbumsAsync_NoFallback_WhenSpotifyActiveWithCredentials()
        {
            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.Spotify });
            FakeSeriesDataService series = new();
            FakeEpisodeDataService episodes = new();
            ImportService service = BuildService(settings, series, episodes,
                spotifySearch: new FakeSeriesImportSearch([], "Spotify"),
                appleMusicSearch: new FakeSeriesImportSearch([], "AppleMusic"),
                spotifyEpisodeSource: new FakeEpisodeImportSource([]),
                appleMusicEpisodeSource: new FakeEpisodeImportSource([]));

            SearchOutcome result = await service.SearchAlbumsAsync("query");

            Assert.False(result.SpotifyFallbackApplied);
        }

        [Fact]
        public async Task SearchAlbumsAsync_NoFallback_WhenQueryIsEmpty()
        {
            FakeAppSettingsDataService settings = new(new AppSettings { ActiveProvider = ProviderType.Spotify });
            FakeSeriesDataService series = new();
            FakeEpisodeDataService episodes = new();
            ImportService service = BuildService(settings, series, episodes,
                spotifySearch: new FakeSeriesImportSearch([], "Spotify"),
                appleMusicSearch: new FakeSeriesImportSearch([], "AppleMusic"),
                spotifyEpisodeSource: new FakeEpisodeImportSource([]),
                appleMusicEpisodeSource: new FakeEpisodeImportSource([]),
                credentialsProvider: FakeSpotifyClientCredentialsProvider.Missing());

            SearchOutcome result = await service.SearchAlbumsAsync("   ");

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
            ImportService service = BuildService(settings, seriesService, episodeService,
                spotifySearch: new FakeSeriesImportSearch([], "Spotify"),
                appleMusicSearch: new FakeSeriesImportSearch([], "AppleMusic"),
                spotifyEpisodeSource: new FakeEpisodeImportSource([]),
                appleMusicEpisodeSource: new FakeEpisodeImportSource([]));

            ImportSeries importSeries = new()
            {
                SourceSeriesId = "artist-123",
                Source = "AppleMusic",
                Title = "Die drei ???",
                IsHoerspiel = true,
                Score = 80
            };

            _ = await service.ImportAsync(importSeries);

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

            ImportService service = BuildService(settings, seriesService, episodeService,
                spotifySearch: new FakeSeriesImportSearch([], "Spotify"),
                appleMusicSearch: new FakeSeriesImportSearch([], "AppleMusic"),
                spotifyEpisodeSource: new FakeEpisodeImportSource([]),
                appleMusicEpisodeSource: new FakeEpisodeImportSource(importEpisodes));

            ImportSeries importSeries = new()
            {
                SourceSeriesId = "artist-123",
                Source = "AppleMusic",
                Title = "TKKG",
                IsHoerspiel = true,
                Score = 90
            };

            _ = await service.ImportAsync(importSeries);

            Assert.Equal(3, episodeService.All.Count);
        }

        [Fact]
        public async Task ImportAsync_DeduplicatesEpisodes_BySourceEpisodeId()
        {
            // Brief 268: Provider liefert dieselbe Folge mehrfach (Compilation/Re-Release).
            // Nach dem Import darf in der DB nur eine Episode pro SourceEpisodeId stehen.
            FakeSeriesDataService seriesService = new();
            FakeEpisodeDataService episodeService = new();
            FakeAppSettingsDataService settings = new(new AppSettings());

            IReadOnlyList<ImportEpisode> importEpisodesWithDuplicates =
            [
                new() { SourceEpisodeId = "album-1", Title = "Folge 1: Auftakt", EpisodeNumber = 1 },
                new() { SourceEpisodeId = "album-2", Title = "Folge 2: Spuren", EpisodeNumber = 2 },
                new() { SourceEpisodeId = "album-1", Title = "Folge 1: Auftakt (Re-Release)", EpisodeNumber = 1 },
                new() { SourceEpisodeId = "album-3", Title = "Folge 3: Verfolgung", EpisodeNumber = 3 },
                new() { SourceEpisodeId = "album-2", Title = "Folge 2: Spuren (Compilation)", EpisodeNumber = 2 },
            ];

            ImportService service = BuildService(settings, seriesService, episodeService,
                spotifySearch: new FakeSeriesImportSearch([], "Spotify"),
                appleMusicSearch: new FakeSeriesImportSearch([], "AppleMusic"),
                spotifyEpisodeSource: new FakeEpisodeImportSource([]),
                appleMusicEpisodeSource: new FakeEpisodeImportSource(importEpisodesWithDuplicates));

            ImportSeries importSeries = new()
            {
                SourceSeriesId = "artist-scotland-yard",
                Source = "AppleMusic",
                Title = "Scotland Yard",
                IsHoerspiel = true,
                Score = 80
            };

            _ = await service.ImportAsync(importSeries);

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

            ImportService service = BuildService(settings, seriesService, episodeService,
                spotifySearch: new FakeSeriesImportSearch([], "Spotify"),
                appleMusicSearch: new FakeSeriesImportSearch([], "AppleMusic"),
                spotifyEpisodeSource: new FakeEpisodeImportSource([]),
                appleMusicEpisodeSource: new FakeEpisodeImportSource(importEpisodes));

            ImportSeries importSeries = new()
            {
                SourceSeriesId = "artist-clean",
                Source = "AppleMusic",
                Title = "Saubere Serie",
                IsHoerspiel = true,
                Score = 70
            };

            _ = await service.ImportAsync(importSeries);

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
                AppleMusicArtistId = "artist-existing"
            });

            ImportService service = BuildService(settings, seriesService, episodeService,
                spotifySearch: new FakeSeriesImportSearch([], "Spotify"),
                appleMusicSearch: new FakeSeriesImportSearch([], "AppleMusic"),
                spotifyEpisodeSource: new FakeEpisodeImportSource([]),
                appleMusicEpisodeSource: new FakeEpisodeImportSource([]));

            ImportSeries importSeries = new()
            {
                SourceSeriesId = "artist-existing",
                Source = "AppleMusic",
                Title = "Bestehende Serie",
                IsHoerspiel = true,
                Score = 70
            };

            _ = await service.ImportAsync(importSeries);

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
            ImportService service = BuildService(settings, seriesService, episodeService,
                spotifySearch: new FakeSeriesImportSearch([], "Spotify"),
                appleMusicSearch: new FakeSeriesImportSearch([], "AppleMusic"),
                spotifyEpisodeSource: new FakeEpisodeImportSource([]),
                appleMusicEpisodeSource: new FakeEpisodeImportSource([]));

            ImportSeries importSeries = new()
            {
                SourceSeriesId = "artist-new",
                Source = "AppleMusic",
                Title = "Neue Serie",
                IsHoerspiel = true,
                Score = 60
            };

            Guid id = await service.ImportAsync(importSeries);

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
                SpotifyArtistId = "spotify-123"
            });

            ImportService service = BuildService(settings, seriesService, episodeService,
                spotifySearch: new FakeSeriesImportSearch([], "Spotify"),
                appleMusicSearch: new FakeSeriesImportSearch([], "AppleMusic"),
                spotifyEpisodeSource: new FakeEpisodeImportSource([]),
                appleMusicEpisodeSource: new FakeEpisodeImportSource([]));

            ImportSeries series = new()
            {
                SourceSeriesId = "spotify-123",
                Source = "Spotify",
                Title = "Vorhandene Serie",
                IsHoerspiel = true,
                Score = 75
            };

            bool result = await service.IsAlreadyImportedAsync(series);

            Assert.True(result);
        }

        [Fact]
        public async Task IsAlreadyImportedAsync_ReturnsFalse_WhenSeriesNotFound()
        {
            // Unbekannte SourceSeriesId darf nicht als "bereits vorhanden" erkannt werden
            FakeSeriesDataService seriesService = new();
            FakeAppSettingsDataService settings = new(new AppSettings());
            FakeEpisodeDataService episodeService = new();
            ImportService service = BuildService(settings, seriesService, episodeService,
                spotifySearch: new FakeSeriesImportSearch([], "Spotify"),
                appleMusicSearch: new FakeSeriesImportSearch([], "AppleMusic"),
                spotifyEpisodeSource: new FakeEpisodeImportSource([]),
                appleMusicEpisodeSource: new FakeEpisodeImportSource([]));

            ImportSeries series = new()
            {
                SourceSeriesId = "unbekannt-999",
                Source = "Spotify",
                Title = "Nicht vorhanden",
                IsHoerspiel = true,
                Score = 40
            };

            bool result = await service.IsAlreadyImportedAsync(series);

            Assert.False(result);
        }
    }
}
