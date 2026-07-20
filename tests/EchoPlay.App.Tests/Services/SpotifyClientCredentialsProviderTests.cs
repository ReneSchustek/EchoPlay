using EchoPlay.App.Services;
using EchoPlay.Spotify.Auth;
using EchoPlay.Spotify.Configuration;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="SpotifyClientCredentialsProvider"/>: adaptiert die vollständigen
    /// <see cref="SpotifyOptions"/> auf die vom Token-Client benötigten
    /// <see cref="SpotifyClientCredentials"/>. Liefert nur bei vollständigen Credentials ein Ergebnis.
    /// </summary>
    public sealed class SpotifyClientCredentialsProviderTests
    {
        [Fact]
        public async Task GetAsync_ReturnsCredentials_WhenOptionsComplete()
        {
            StubOptionsProvider options = new(new SpotifyOptions { ClientId = "abc", ClientSecret = "xyz" });
            SpotifyClientCredentialsProvider provider = new(options);

            SpotifyClientCredentials? result = await provider.GetAsync(TestContext.Current.CancellationToken);

            Assert.NotNull(result);
            Assert.Equal("abc", result!.ClientId);
            Assert.Equal("xyz", result.ClientSecret);
        }

        [Fact]
        public async Task GetAsync_ReturnsNull_WhenOptionsAreNull()
        {
            StubOptionsProvider options = new(null);
            SpotifyClientCredentialsProvider provider = new(options);

            SpotifyClientCredentials? result = await provider.GetAsync(TestContext.Current.CancellationToken);

            Assert.Null(result);
        }

        [Fact]
        public async Task GetAsync_ReturnsNull_WhenClientIdEmpty()
        {
            StubOptionsProvider options = new(new SpotifyOptions { ClientId = "", ClientSecret = "xyz" });
            SpotifyClientCredentialsProvider provider = new(options);

            SpotifyClientCredentials? result = await provider.GetAsync(TestContext.Current.CancellationToken);

            Assert.Null(result);
        }

        [Fact]
        public async Task GetAsync_ReturnsNull_WhenClientSecretEmpty()
        {
            StubOptionsProvider options = new(new SpotifyOptions { ClientId = "abc", ClientSecret = "" });
            SpotifyClientCredentialsProvider provider = new(options);

            SpotifyClientCredentials? result = await provider.GetAsync(TestContext.Current.CancellationToken);

            Assert.Null(result);
        }

        /// <summary>Minimaler <see cref="ISpotifyOptionsProvider"/>-Stub mit fest vorgegebenem Ergebnis.</summary>
        private sealed class StubOptionsProvider(SpotifyOptions? options) : ISpotifyOptionsProvider
        {
            public bool IsAvailable => options is not null;

            public Task<SpotifyOptions?> GetAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(options);
        }
    }
}
