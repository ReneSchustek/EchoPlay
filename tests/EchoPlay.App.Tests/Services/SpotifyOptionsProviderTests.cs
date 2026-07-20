using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Spotify.Configuration;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="SpotifyOptionsProvider"/>: kombiniert statische Basis-URLs
    /// mit den Credentials aus dem <see cref="ISpotifyCredentialStore"/>.
    /// </summary>
    public sealed class SpotifyOptionsProviderTests
    {
        private static SpotifyOptions BaseOptions => new()
        {
            ApiBaseUrl = "https://api.spotify.com/v1",
            AuthBaseUrl = "https://accounts.spotify.com/api/token"
        };

        [Fact]
        public async Task GetAsync_MergesBaseUrlsWithCredentials_WhenCredentialsExist()
        {
            FakeSpotifyCredentialStore store = new();
            await store.SaveAsync("client-id-42", "secret-99", TestContext.Current.CancellationToken);
            SpotifyOptionsProvider provider = new(BaseOptions, store);

            SpotifyOptions? result = await provider.GetAsync(TestContext.Current.CancellationToken);

            Assert.NotNull(result);
            Assert.Equal("https://api.spotify.com/v1", result!.ApiBaseUrl);
            Assert.Equal("https://accounts.spotify.com/api/token", result.AuthBaseUrl);
            Assert.Equal("client-id-42", result.ClientId);
            Assert.Equal("secret-99", result.ClientSecret);
        }

        [Fact]
        public async Task GetAsync_ReturnsNull_WhenNoCredentialsStored()
        {
            FakeSpotifyCredentialStore store = new();
            SpotifyOptionsProvider provider = new(BaseOptions, store);

            SpotifyOptions? result = await provider.GetAsync(TestContext.Current.CancellationToken);

            Assert.Null(result);
        }

        [Fact]
        public async Task IsAvailable_ReflectsCredentialStoreState()
        {
            FakeSpotifyCredentialStore store = new();
            SpotifyOptionsProvider provider = new(BaseOptions, store);

            Assert.False(provider.IsAvailable);

            await store.SaveAsync("id", "secret", TestContext.Current.CancellationToken);

            Assert.True(provider.IsAvailable);
        }
    }
}
