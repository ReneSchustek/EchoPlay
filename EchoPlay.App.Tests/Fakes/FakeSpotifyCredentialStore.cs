using EchoPlay.App.Services;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ISpotifyCredentialStore"/>. Speichert Credentials im Speicher
    /// ohne DPAPI-Verschlüsselung, damit Tests ohne Windows-Benutzerprofil laufen können.
    /// </summary>
    internal sealed class FakeSpotifyCredentialStore : ISpotifyCredentialStore
    {
        private (string ClientId, string ClientSecret)? _credentials;

        /// <summary>Anzahl der SaveAsync-Aufrufe.</summary>
        public int SaveCallCount { get; private set; }

        /// <summary>Anzahl der ClearAsync-Aufrufe.</summary>
        public int ClearCallCount { get; private set; }

        /// <inheritdoc/>
        public bool HasCredentials => _credentials is not null;

        /// <inheritdoc/>
        public Task<(string ClientId, string ClientSecret)?> GetAsync()
        {
            return Task.FromResult(_credentials);
        }

        /// <inheritdoc/>
        public Task SaveAsync(string clientId, string clientSecret)
        {
            _credentials = (clientId, clientSecret);
            SaveCallCount++;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task ClearAsync()
        {
            _credentials = null;
            ClearCallCount++;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }
    }
}
