using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Speichert Spotify-Credentials DPAPI-verschlüsselt in der SQLite-Datenbank.
    /// DPAPI ist an den Windows-Benutzer gebunden — die Daten sind nur auf derselben
    /// Maschine und mit demselben Benutzerkonto entschlüsselbar.
    /// </summary>
    public sealed class SpotifyCredentialStore : ISpotifyCredentialStore
    {
        private const string KeyClientId = "Spotify:ClientId";
        private const string KeyClientSecret = "Spotify:ClientSecret";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;

        private bool _hasCredentials;
        private bool _lastLoadFailedDueToCorruption;

        /// <summary>
        /// Erstellt einen neuen Credential-Store.
        /// </summary>
        public SpotifyCredentialStore(
            IServiceScopeFactory scopeFactory,
            ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory;
            _logger = loggerFactory.CreateLogger("SpotifyCredentialStore");
        }

        /// <inheritdoc/>
        public bool HasCredentials => _hasCredentials;

        /// <inheritdoc/>
        public bool LastLoadFailedDueToCorruption => _lastLoadFailedDueToCorruption;

        /// <inheritdoc/>
        public void AcknowledgeCorruptionNotice() => _lastLoadFailedDueToCorruption = false;

        /// <inheritdoc/>
        public async Task<(string ClientId, string ClientSecret)?> GetAsync()
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISecureSettingsDataService service = scope.ServiceProvider
                .GetRequiredService<ISecureSettingsDataService>();

            byte[]? encryptedId = await service.GetAsync(KeyClientId);
            byte[]? encryptedSecret = await service.GetAsync(KeyClientSecret);

            if (encryptedId is null || encryptedSecret is null)
            {
                return null;
            }

            try
            {
                string clientId = Decrypt(encryptedId);
                string clientSecret = Decrypt(encryptedSecret);
                return (clientId, clientSecret);
            }
            catch (CryptographicException ex)
            {
                // Nach Windows-Profil-Migration oder PC-Wechsel sind die Cipher-Bytes nicht mehr
                // entschlüsselbar. Ohne Aufräumen loggt jeder Start denselben Fehler — daher
                // löschen wir die korrupten Records und erzwingen eine Neu-Eingabe durch den Nutzer.
                _logger.Warning($"Spotify-Credentials konnten nicht entschlüsselt werden ({ex.Message}). Korrupte Records werden entfernt.");

                await service.DeleteAsync(KeyClientId).ConfigureAwait(false);
                await service.DeleteAsync(KeyClientSecret).ConfigureAwait(false);

                _hasCredentials = false;
                _lastLoadFailedDueToCorruption = true;
                return null;
            }
        }

        /// <inheritdoc/>
        public async Task SaveAsync(string clientId, string clientSecret)
        {
            byte[] encryptedId = Encrypt(clientId);
            byte[] encryptedSecret = Encrypt(clientSecret);

            using IServiceScope scope = _scopeFactory.CreateScope();
            ISecureSettingsDataService service = scope.ServiceProvider
                .GetRequiredService<ISecureSettingsDataService>();

            await service.SaveAsync(KeyClientId, encryptedId);
            await service.SaveAsync(KeyClientSecret, encryptedSecret);

            _hasCredentials = true;
            _lastLoadFailedDueToCorruption = false;
            _logger.Info("Spotify-Credentials gespeichert.");
        }

        /// <inheritdoc/>
        public async Task ClearAsync()
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISecureSettingsDataService service = scope.ServiceProvider
                .GetRequiredService<ISecureSettingsDataService>();

            await service.DeleteAsync(KeyClientId);
            await service.DeleteAsync(KeyClientSecret);

            _hasCredentials = false;
            _lastLoadFailedDueToCorruption = false;
            _logger.Info("Spotify-Credentials gelöscht.");
        }

        /// <inheritdoc/>
        public async Task InitializeAsync()
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISecureSettingsDataService service = scope.ServiceProvider
                .GetRequiredService<ISecureSettingsDataService>();

            byte[]? encryptedId = await service.GetAsync(KeyClientId);
            _hasCredentials = encryptedId is not null;
        }

        private static byte[] Encrypt(string plainText)
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            return ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        }

        private static string Decrypt(byte[] encryptedBytes)
        {
            byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
    }
}
