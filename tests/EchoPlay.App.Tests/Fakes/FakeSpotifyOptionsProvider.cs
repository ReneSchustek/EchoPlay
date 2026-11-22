using EchoPlay.App.Services;
using EchoPlay.Spotify.Configuration;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ISpotifyOptionsProvider"/>. Gibt vorkonfigurierte SpotifyOptions
    /// zurück oder <see langword="null"/>, wenn keine Credentials vorhanden sind.
    /// </summary>
    internal sealed class FakeSpotifyOptionsProvider : ISpotifyOptionsProvider
    {
        private readonly ISpotifyCredentialStore _credentialStore;

        public FakeSpotifyOptionsProvider(ISpotifyCredentialStore credentialStore)
        {
            _credentialStore = credentialStore;
        }

        /// <inheritdoc/>
        public bool IsAvailable => _credentialStore.HasCredentials;

        /// <inheritdoc/>
        public async Task<SpotifyOptions?> GetAsync(CancellationToken cancellationToken = default)
        {
            (string ClientId, string ClientSecret)? credentials = await _credentialStore.GetAsync(cancellationToken);

            if (credentials is null)
            {
                return null;
            }

            return new SpotifyOptions
            {
                ApiBaseUrl = new Uri("https://api.spotify.com/v1"),
                AuthBaseUrl = new Uri("https://accounts.spotify.com/api/token"),
                ClientId = credentials.Value.ClientId,
                ClientSecret = credentials.Value.ClientSecret
            };
        }
    }
}
