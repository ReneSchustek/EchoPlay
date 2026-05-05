using EchoPlay.Spotify.Configuration;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Kombiniert die öffentlichen Spotify-URLs aus <see cref="SpotifyOptions"/> mit den
    /// DPAPI-verschlüsselten Credentials aus dem <see cref="ISpotifyCredentialStore"/>.
    /// </summary>

    public sealed class SpotifyOptionsProvider : ISpotifyOptionsProvider
    {
        private readonly SpotifyOptions _baseOptions;
        private readonly ISpotifyCredentialStore _credentialStore;

        /// <summary>
        /// Erstellt den Provider mit den statischen Basis-URLs und dem Credential-Store.
        /// </summary>
        /// <param name="baseOptions">
        /// Basis-Optionen aus <c>appsettings.json</c> — enthält nur ApiBaseUrl und AuthBaseUrl.
        /// </param>
        /// <param name="credentialStore">Store für die verschlüsselten Credentials.</param>

        public SpotifyOptionsProvider(SpotifyOptions baseOptions, ISpotifyCredentialStore credentialStore)
        {
            _baseOptions = baseOptions;
            _credentialStore = credentialStore;
        }

        /// <inheritdoc/>
        public bool IsAvailable => _credentialStore.HasCredentials;

        /// <inheritdoc/>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<SpotifyOptions?> GetAsync(CancellationToken cancellationToken = default)
        {
            (string ClientId, string ClientSecret)? credentials = await _credentialStore.GetAsync(cancellationToken);

            if (credentials is null)
            {
                return null;
            }

            return new SpotifyOptions
            {
                ApiBaseUrl = _baseOptions.ApiBaseUrl,
                AuthBaseUrl = _baseOptions.AuthBaseUrl,
                ClientId = credentials.Value.ClientId,
                ClientSecret = credentials.Value.ClientSecret
            };
        }
    }
}
