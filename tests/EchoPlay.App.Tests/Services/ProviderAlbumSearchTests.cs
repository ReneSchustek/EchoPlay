using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.Auth;
using EchoPlay.Spotify.Dtos;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für die Album-Suche beim Anbieter: Ein Treffer ist eine einzelne Folge,
    /// keine Serie — die Abbildung auf <see cref="ImportSeries"/> entscheidet, was
    /// die Trefferkachel anzeigt und was beim Übernehmen in der Ablage landet.
    /// </summary>
    /// <remarks>
    /// Die Anbieterwahl samt Rückfall auf Apple Music steht in
    /// <see cref="ImportServiceTests"/>; hier geht es um die beiden Suchwege selbst.
    /// </remarks>
    public sealed class ProviderAlbumSearchTests
    {
        private static ImportService BuildService(
            ProviderType activeProvider,
            FakeSpotifyApiClient? spotifyClient = null,
            FakeAppleMusicSearchClient? appleClient = null,
            FakeSpotifyClientCredentialsProvider? credentialsProvider = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped<IAppSettingsDataService>(
                _ => new FakeAppSettingsDataService(new AppSettings { ActiveProvider = activeProvider }));
            _ = services.AddScoped<ISeriesDataService>(_ => new FakeSeriesDataService());
            _ = services.AddScoped<IEpisodeDataService>(_ => new FakeEpisodeDataService());
            _ = services.AddScoped<IWatchedTitleDataService>(_ => new FakeWatchedTitleDataService());
            _ = services.AddScoped<ICoverImageDataService>(_ => new FakeCoverImageDataService());
            _ = services.AddKeyedScoped<ISeriesImportSearch>("Spotify", (_, _) => new FakeSeriesImportSearch([], "Spotify"));
            _ = services.AddKeyedScoped<ISeriesImportSearch>("AppleMusic", (_, _) => new FakeSeriesImportSearch([], "AppleMusic"));
            _ = services.AddKeyedScoped<IEpisodeImportSource>("Spotify", (_, _) => new FakeEpisodeImportSource([]));
            _ = services.AddKeyedScoped<IEpisodeImportSource>("AppleMusic", (_, _) => new FakeEpisodeImportSource([]));
            _ = services.AddSingleton<ISpotifyClientCredentialsProvider>(
                credentialsProvider ?? FakeSpotifyClientCredentialsProvider.WithCredentials());

            if (spotifyClient is not null)
            {
                _ = services.AddScoped<ISpotifyApiClient>(_ => spotifyClient);
            }

            if (appleClient is not null)
            {
                _ = services.AddScoped<IAppleMusicSearchClient>(_ => appleClient);
            }

            _ = services.AddSingleton<ILoggerFactory>(new FakeLoggerFactory());
            _ = services.AddSingleton<IClock>(new FakeClock());
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

        [Fact]
        public async Task Spotify_LiefertAlbenAlsEinzelneFolgen()
        {
            FakeSpotifyApiClient spotify = new();
            spotify.Albums.Add(new SpotifyAlbumDto
            {
                SpotifyAlbumId = "album-1",
                Title = "Der Superhund",
                ArtistName = "TKKG",
                ImageUrl = "https://example.com/cover.jpg"
            });

            ImportService service = BuildService(ProviderType.Spotify, spotifyClient: spotify);

            SearchOutcome outcome = await service.SearchAlbumsAsync("TKKG", TestContext.Current.CancellationToken);

            ImportSeries treffer = Assert.Single(outcome.Results);
            Assert.Equal("album-1", treffer.SourceSeriesId);
            Assert.Equal(ProviderKeys.Spotify, treffer.Source);
            Assert.Equal("Der Superhund", treffer.Title);
            Assert.Equal("TKKG", treffer.ArtistName);
            Assert.Equal("https://example.com/cover.jpg", treffer.CoverImageUrl);
            Assert.True(treffer.IsAlbumResult);
            Assert.Equal(("TKKG", 15), Assert.Single(spotify.AlbumSearches));
        }

        [Fact]
        public async Task AppleMusic_LiefertAlbenAlsEinzelneFolgen()
        {
            FakeAppleMusicSearchClient apple = new();
            apple.AlbumSearchResults.Add(new ITunesCollectionDto
            {
                WrapperType = "collection",
                CollectionId = 4711,
                CollectionName = "Der Superhund",
                ArtistName = "TKKG"
            });

            ImportService service = BuildService(ProviderType.AppleMusic, appleClient: apple);

            SearchOutcome outcome = await service.SearchAlbumsAsync("TKKG", TestContext.Current.CancellationToken);

            ImportSeries treffer = Assert.Single(outcome.Results);
            Assert.Equal("4711", treffer.SourceSeriesId);
            Assert.Equal(ProviderKeys.AppleMusic, treffer.Source);
            Assert.Equal("Der Superhund", treffer.Title);
            Assert.Equal("TKKG", treffer.ArtistName);
            Assert.True(treffer.IsAlbumResult);
            Assert.Equal("TKKG", Assert.Single(apple.AlbumSearchQueries));
        }

        [Fact]
        public async Task AppleMusic_UebergehtEintraegeDieKeinAlbumSind()
        {
            // Die Antwort mischt Künstler und Alben. Ein Künstlereintrag als Folge
            // ausgegeben führte beim Übernehmen zu einer Serie ohne Inhalt.
            FakeAppleMusicSearchClient apple = new();
            apple.AlbumSearchResults.Add(new ITunesCollectionDto
            {
                WrapperType = "artist",
                CollectionId = 1,
                CollectionName = "TKKG",
                ArtistName = "TKKG"
            });
            apple.AlbumSearchResults.Add(new ITunesCollectionDto
            {
                WrapperType = "collection",
                CollectionId = 2,
                CollectionName = "Der Superhund",
                ArtistName = "TKKG"
            });

            ImportService service = BuildService(ProviderType.AppleMusic, appleClient: apple);

            SearchOutcome outcome = await service.SearchAlbumsAsync("TKKG", TestContext.Current.CancellationToken);

            Assert.Equal(["Der Superhund"], outcome.Results.Select(r => r.Title));
        }

        [Fact]
        public async Task OhneAnbieter_BleibtDieAlbenSucheLeer()
        {
            ImportService service = BuildService(ProviderType.None, spotifyClient: new FakeSpotifyApiClient());

            SearchOutcome outcome = await service.SearchAlbumsAsync("TKKG", TestContext.Current.CancellationToken);

            Assert.Empty(outcome.Results);
            Assert.False(outcome.SpotifyFallbackApplied);
        }

        [Fact]
        public async Task OhneRegistriertenZugang_BleibtDieAlbenSucheLeer()
        {
            // Ohne registrierten Anbieter-Zugang darf die Suche nichts liefern und
            // nicht reißen — die Trefferliste bleibt schlicht leer.
            ImportService service = BuildService(ProviderType.Spotify);

            SearchOutcome outcome = await service.SearchAlbumsAsync("TKKG", TestContext.Current.CancellationToken);

            Assert.Empty(outcome.Results);
        }

        [Fact]
        public async Task OhneZugangsdaten_SuchtSpotifyAnfrageUeberAppleMusic()
        {
            // Derselbe Rückfall wie bei der Serien-Suche: Spotify ist gewählt, aber ohne
            // Zugangsdaten unbrauchbar — gefragt wird dann Apple Music.
            FakeSpotifyApiClient spotify = new();
            FakeAppleMusicSearchClient apple = new();
            apple.AlbumSearchResults.Add(new ITunesCollectionDto
            {
                WrapperType = "collection",
                CollectionId = 99,
                CollectionName = "Der Superhund",
                ArtistName = "TKKG"
            });

            ImportService service = BuildService(
                ProviderType.Spotify,
                spotifyClient: spotify,
                appleClient: apple,
                credentialsProvider: FakeSpotifyClientCredentialsProvider.Missing());

            SearchOutcome outcome = await service.SearchAlbumsAsync("TKKG", TestContext.Current.CancellationToken);

            Assert.True(outcome.SpotifyFallbackApplied);
            Assert.Equal("99", Assert.Single(outcome.Results).SourceSeriesId);
            Assert.Empty(spotify.AlbumSearches);
        }

        [Fact]
        public async Task BeideAnbieter_FragenAppleMusic()
        {
            // "Beide" heißt nicht "beide gleichzeitig": Die Suche entscheidet sich für
            // Apple Music, weil dort keine Zugangsdaten nötig sind.
            FakeSpotifyApiClient spotify = new();
            spotify.Albums.Add(new SpotifyAlbumDto
            {
                SpotifyAlbumId = "album-1",
                Title = "Der Superhund",
                ArtistName = "TKKG"
            });
            FakeAppleMusicSearchClient apple = new();
            apple.AlbumSearchResults.Add(new ITunesCollectionDto
            {
                WrapperType = "collection",
                CollectionId = 7,
                CollectionName = "Der Superhund",
                ArtistName = "TKKG"
            });

            ImportService service = BuildService(
                ProviderType.Both,
                spotifyClient: spotify,
                appleClient: apple);

            SearchOutcome outcome = await service.SearchAlbumsAsync("TKKG", TestContext.Current.CancellationToken);

            IReadOnlyList<ImportSeries> results = outcome.Results;
            Assert.Equal("7", Assert.Single(results).SourceSeriesId);
            Assert.Empty(spotify.AlbumSearches);
            Assert.False(outcome.SpotifyFallbackApplied);
        }
    }
}
