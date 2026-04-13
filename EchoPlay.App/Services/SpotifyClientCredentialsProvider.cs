using EchoPlay.Spotify.Auth;
using EchoPlay.Spotify.Configuration;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Adapter zwischen dem App-seitigen <see cref="ISpotifyOptionsProvider"/> (liefert
    /// vollständige <see cref="SpotifyOptions"/>) und dem Domain-Interface
    /// <see cref="ISpotifyClientCredentialsProvider"/>, das der Token-Client in
    /// <c>EchoPlay.Spotify</c> nutzt. Entkoppelt die Spotify-Library vom App-Credential-Store.
    /// </summary>
    public sealed class SpotifyClientCredentialsProvider : ISpotifyClientCredentialsProvider
    {
        private readonly ISpotifyOptionsProvider _optionsProvider;

        /// <summary>
        /// Erstellt den Adapter mit dem App-Options-Provider als Quelle.
        /// </summary>
        public SpotifyClientCredentialsProvider(ISpotifyOptionsProvider optionsProvider)
        {
            _optionsProvider = optionsProvider;
        }

        /// <inheritdoc/>
        public async Task<SpotifyClientCredentials?> GetAsync(CancellationToken cancellationToken = default)
        {
            SpotifyOptions? options = await _optionsProvider.GetAsync().ConfigureAwait(false);

            if (options is null || string.IsNullOrEmpty(options.ClientId) || string.IsNullOrEmpty(options.ClientSecret))
            {
                return null;
            }

            return new SpotifyClientCredentials(options.ClientId, options.ClientSecret);
        }
    }
}
