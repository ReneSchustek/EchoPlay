using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Configuration;
using EchoPlay.Logger.Core;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="SpotifyCredentialStore"/>.
    /// Nutzt <see cref="FakeSecureSettingsDataService"/> statt der echten DB,
    /// da DPAPI nur auf derselben Windows-Maschine funktioniert.
    /// Die DPAPI-Verschlüsselung wird trotzdem real ausgeführt, weil der Test auf Windows läuft.
    /// </summary>
    public sealed class SpotifyCredentialStoreTests
    {
        private static (SpotifyCredentialStore Store, FakeSecureSettingsDataService FakeService) BuildStore()
        {
            FakeSecureSettingsDataService fakeService = new();

            ServiceCollection services = new();
            _ = services.AddScoped<ISecureSettingsDataService>(_ => fakeService);

            ServiceProvider provider          = services.BuildServiceProvider();
            IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            LoggerOptions options       = new() { EnableFileLogging = false, EnableAutoCleanup = false };
            LoggerFactory loggerFactory = new([], options);

            SpotifyCredentialStore store = new(scopeFactory, loggerFactory);
            return (store, fakeService);
        }

        [Fact]
        public async Task SaveAsync_SetsHasCredentials()
        {
            // Nach dem Speichern muss HasCredentials true sein
            (SpotifyCredentialStore store, _) = BuildStore();

            Assert.False(store.HasCredentials);

            await store.SaveAsync("test-client-id", "test-client-secret");

            Assert.True(store.HasCredentials);
        }

        [Fact]
        public async Task ClearAsync_RemovesCredentials()
        {
            // Nach dem Löschen muss HasCredentials false sein
            (SpotifyCredentialStore store, _) = BuildStore();

            await store.SaveAsync("test-client-id", "test-client-secret");
            Assert.True(store.HasCredentials);

            await store.ClearAsync();
            Assert.False(store.HasCredentials);
        }

        [Fact]
        public async Task GetAsync_ReturnsNull_WhenEmpty()
        {
            // Ohne gespeicherte Credentials muss GetAsync null zurückgeben
            (SpotifyCredentialStore store, _) = BuildStore();

            (string ClientId, string ClientSecret)? result = await store.GetAsync();

            Assert.Null(result);
        }

        [Fact]
        public async Task SaveAsync_ThenGetAsync_ReturnsCredentials()
        {
            // Gespeicherte Credentials müssen nach dem Lesen wieder identisch sein
            (SpotifyCredentialStore store, _) = BuildStore();

            await store.SaveAsync("meine-client-id", "mein-secret");

            (string ClientId, string ClientSecret)? result = await store.GetAsync();

            _ = Assert.NotNull(result);
            Assert.Equal("meine-client-id", result.Value.ClientId);
            Assert.Equal("mein-secret", result.Value.ClientSecret);
        }

        [Fact]
        public async Task InitializeAsync_SetsHasCredentials_WhenDataExists()
        {
            // InitializeAsync muss den Cache korrekt setzen
            (SpotifyCredentialStore store, _) = BuildStore();

            await store.SaveAsync("id", "secret");

            // Neuen Store mit demselben FakeService erstellen, um InitializeAsync zu testen
            (SpotifyCredentialStore freshStore, FakeSecureSettingsDataService fakeService) = BuildStore();

            // Daten manuell in den FakeService eintragen (simuliert DB-Zustand)
            await fakeService.SaveAsync("Spotify:ClientId", new byte[] { 1, 2, 3 });

            await freshStore.InitializeAsync();

            Assert.True(freshStore.HasCredentials);
        }

        [Fact]
        public async Task InitializeAsync_SetsFalse_WhenNoData()
        {
            // Ohne Daten muss InitializeAsync HasCredentials auf false setzen
            (SpotifyCredentialStore store, _) = BuildStore();

            await store.InitializeAsync();

            Assert.False(store.HasCredentials);
        }
    }
}
