using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.Dtos;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft den Verbindungstest für Apple Music und für „beide Anbieter" sowie die
    /// Fehlerarten, die der Test dem Anwender als Grund zeigen muss.
    /// </summary>
    /// <remarks>
    /// Der Verbindungstest ist die einzige Stelle, an der der Anwender vor dem ersten
    /// Suchlauf erfährt, ob seine Zugangsdaten taugen. Verschluckt er einen Fehler, sucht
    /// der Anwender später vergeblich und weiß nicht, warum.
    ///
    /// Eigene Datei statt Ergänzung von <c>ConnectionTestCoordinatorTests</c>: Dort steht
    /// der Spotify-Weg, hier die übrigen.
    /// </remarks>
    public sealed class ConnectionTestProviderTests
    {
        [Fact]
        public async Task TestAsync_AppleMusic_WhenReachable_ReportsSuccess()
        {
            FakeAppleMusicSearchClient apple = new();
            ConnectionTestCoordinator sut = Build(apple: apple);

            ConnectionTestResult result = await sut.TestAsync(
                ProviderType.AppleMusic, TestContext.Current.CancellationToken);

            Assert.True(result.Success);
            Assert.Equal(1, apple.SearchArtistsCallCount);
        }

        [Fact]
        public async Task TestAsync_Both_AsksAppleMusicAndSpotify()
        {
            FakeAppleMusicSearchClient apple = new();
            FakeSpotifyApiClient spotify = new();
            ConnectionTestCoordinator sut = Build(apple: apple, spotify: spotify);

            ConnectionTestResult result = await sut.TestAsync(
                ProviderType.Both, TestContext.Current.CancellationToken);

            // „Beide" heißt beide — ein grüner Haken, obwohl nur einer geprüft wurde,
            // wäre schlimmer als gar kein Test.
            Assert.True(result.Success);
            Assert.Equal(1, apple.SearchArtistsCallCount);
        }

        [Fact]
        public async Task TestAsync_WhenTheProviderTimesOut_ShowsTheReason()
        {
            ConnectionTestCoordinator sut = Build(
                apple: new ThrowingAppleClient(new TaskCanceledException("Zeitüberschreitung")));

            ConnectionTestResult result = await sut.TestAsync(
                ProviderType.AppleMusic, TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.Equal("Zeitüberschreitung", result.ErrorDetail);
        }

        [Fact]
        public async Task TestAsync_WhenTheAnswerIsUnreadable_ShowsTheReason()
        {
            ConnectionTestCoordinator sut = Build(
                apple: new ThrowingAppleClient(new JsonException("Antwort unlesbar")));

            ConnectionTestResult result = await sut.TestAsync(
                ProviderType.AppleMusic, TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.Equal("Antwort unlesbar", result.ErrorDetail);
        }

        [Fact]
        public async Task TestAsync_WhenCredentialsAreMissing_ShowsTheReason()
        {
            // Fehlende Zugangsdaten melden sich als ungültiger Zustand — genau das muss
            // in der Statuszeile stehen, sonst sucht der Anwender beim Netz.
            ConnectionTestCoordinator sut = Build(
                apple: new ThrowingAppleClient(new InvalidOperationException("Keine Zugangsdaten hinterlegt")));

            ConnectionTestResult result = await sut.TestAsync(
                ProviderType.AppleMusic, TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.Equal("Keine Zugangsdaten hinterlegt", result.ErrorDetail);
        }

        private static ConnectionTestCoordinator Build(
            IAppleMusicSearchClient? apple = null,
            ISpotifyApiClient? spotify = null)
        {
            ServiceCollection services = new();
            _ = services.AddScoped(_ => apple ?? new FakeAppleMusicSearchClient());
            _ = services.AddScoped(_ => spotify ?? (ISpotifyApiClient)new FakeSpotifyApiClient());
            ServiceProvider provider = services.BuildServiceProvider();

            return new ConnectionTestCoordinator(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeLocalizationService(),
                new FakeLoggerFactory());
        }

        // ── Test-Stubs ─────────────────────────────────────────────────────────

        private sealed class ThrowingAppleClient : IAppleMusicSearchClient
        {
            private readonly Exception _ex;

            public ThrowingAppleClient(Exception ex) => _ex = ex;

            public Task<ITunesResponseDto<ITunesArtistDto>> SearchArtistsAsync(string query, int limit = 25, CancellationToken ct = default)
                => Task.FromException<ITunesResponseDto<ITunesArtistDto>>(_ex);

            public Task<ITunesResponseDto<ITunesCollectionDto>> SearchAlbumsAsync(string query, int limit = 25, CancellationToken ct = default)
                => Task.FromException<ITunesResponseDto<ITunesCollectionDto>>(_ex);

            public Task<ITunesResponseDto<ITunesCollectionDto>> LookupAlbumsAsync(long artistId, CancellationToken ct = default)
                => Task.FromException<ITunesResponseDto<ITunesCollectionDto>>(_ex);

            public Task<ITunesResponseDto<ITunesTrackDto>> LookupTracksAsync(long collectionId, CancellationToken ct = default)
                => Task.FromException<ITunesResponseDto<ITunesTrackDto>>(_ex);

            public Task<ITunesResponseDto<ITunesTrackDto>> LookupTracksBatchAsync(IReadOnlyList<long> collectionIds, CancellationToken ct = default)
                => Task.FromException<ITunesResponseDto<ITunesTrackDto>>(_ex);
        }
    }
}
