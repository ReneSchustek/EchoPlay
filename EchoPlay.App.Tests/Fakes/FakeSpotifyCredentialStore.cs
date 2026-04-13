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

        /// <summary>
        /// Wenn gesetzt, simuliert GetAsync eine DPAPI-Korruption: Credentials werden
        /// gelöscht, das Corruption-Flag wird gesetzt und null zurückgegeben.
        /// </summary>
        public bool SimulateCryptographicFailure { get; set; }

        /// <inheritdoc/>
        public bool HasCredentials => _credentials is not null;

        /// <inheritdoc/>
        public bool LastLoadFailedDueToCorruption { get; private set; }

        /// <inheritdoc/>
        public void AcknowledgeCorruptionNotice() => LastLoadFailedDueToCorruption = false;

        /// <inheritdoc/>
        public Task<(string ClientId, string ClientSecret)?> GetAsync()
        {
            if (SimulateCryptographicFailure)
            {
                _credentials = null;
                LastLoadFailedDueToCorruption = true;
                return Task.FromResult<(string ClientId, string ClientSecret)?>(null);
            }

            return Task.FromResult(_credentials);
        }

        /// <inheritdoc/>
        public Task SaveAsync(string clientId, string clientSecret)
        {
            _credentials = (clientId, clientSecret);
            LastLoadFailedDueToCorruption = false;
            SaveCallCount++;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task ClearAsync()
        {
            _credentials = null;
            LastLoadFailedDueToCorruption = false;
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
