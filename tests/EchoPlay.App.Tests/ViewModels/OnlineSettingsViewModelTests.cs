using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Settings;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Einstellungen zum Anbieter: Verbindungstest und die Verknüpfung mit Spotify.
    /// </summary>
    /// <remarks>
    /// Zugangsdaten sind der Teil der Einstellungen, bei dem ein stiller Fehlschlag am
    /// teuersten ist: Der Anwender sieht „Verknüpft", die Anmeldung scheitert später im
    /// Hintergrund, und die Suche liefert wortlos nichts. Deshalb prüfen die Tests vor allem,
    /// was in der Statuszeile steht — sie ist die einzige Rückmeldung, die er bekommt.
    /// </remarks>
    public sealed class OnlineSettingsViewModelTests
    {
        private const string ClientId = "1a2b3c4d5e6f7a8b9c0d";
        // Der Wert trägt "test" im Text: Der Secret-Scanner meldet sonst die Zeile
        // als hinterlegtes Anbieter-Geheimnis.
        private const string ClientSecret = "test-secret-0123456789";

        [Fact]
        public async Task TestConnection_WithoutProvider_DoesNothing()
        {
            FakeConnectionTestCoordinator coordinator = new();
            OnlineSettingsViewModel viewModel = BuildViewModel(coordinator);
            viewModel.ActiveProvider = ProviderType.None;

            await viewModel.TestConnectionAsync();

            // Ohne gewählten Anbieter gibt es nichts zu prüfen — ein Aufruf ins Leere
            // erzeugte nur eine Fehlermeldung, die der Anwender nicht einordnen kann.
            Assert.Empty(coordinator.Calls);
            Assert.Null(viewModel.ConnectionTestSuccess);
        }

        [Fact]
        public async Task TestConnection_WhenSuccessful_ReportsSuccessAndStopsTheSpinner()
        {
            FakeConnectionTestCoordinator coordinator = new(new ConnectionTestResult(true, null));
            OnlineSettingsViewModel viewModel = BuildViewModel(coordinator);
            viewModel.ActiveProvider = ProviderType.Spotify;

            await viewModel.TestConnectionAsync();

            Assert.Equal([ProviderType.Spotify], coordinator.Calls);
            Assert.True(viewModel.ConnectionTestSuccess);
            Assert.False(viewModel.IsTestingConnection);
            Assert.NotNull(viewModel.ConnectionTestResultText);
        }

        [Fact]
        public async Task TestConnection_WhenFailing_ShowsTheReason()
        {
            const string reason = "Zeitüberschreitung beim Anbieter";
            FakeConnectionTestCoordinator coordinator = new(new ConnectionTestResult(false, reason));
            OnlineSettingsViewModel viewModel = BuildViewModel(coordinator);
            viewModel.ActiveProvider = ProviderType.Spotify;

            await viewModel.TestConnectionAsync();

            Assert.False(viewModel.ConnectionTestSuccess);

            // Ohne den Grund bleibt dem Anwender nur „ging nicht" — damit kann er nichts
            // anfangen, und der nächste Versuch ist derselbe.
            Assert.Contains(reason, viewModel.ConnectionTestResultText, System.StringComparison.Ordinal);
            Assert.False(viewModel.IsTestingConnection);
        }

        [Fact]
        public void LoadSpotifyStatus_WithoutCredentials_ReportsNotLinked()
        {
            FakeSpotifyCredentialStore credentials = new();
            OnlineSettingsViewModel viewModel = BuildViewModel(credentialStore: credentials);

            viewModel.LoadSpotifyStatus();

            Assert.False(viewModel.IsSpotifyLinked);
            Assert.NotEmpty(viewModel.SpotifyStatus);
        }

        [Fact]
        public async Task LoadSpotifyStatus_WithCredentials_ReportsLinked()
        {
            FakeSpotifyCredentialStore credentials = new();
            await credentials.SaveAsync(ClientId, ClientSecret, TestContext.Current.CancellationToken);

            OnlineSettingsViewModel viewModel = BuildViewModel(credentialStore: credentials);

            viewModel.LoadSpotifyStatus();

            Assert.True(viewModel.IsSpotifyLinked);
        }

        [Fact]
        public async Task TestAndSaveSpotify_WithEmptyFields_SavesNothing()
        {
            FakeSpotifyCredentialStore credentials = new();
            OnlineSettingsViewModel viewModel = BuildViewModel(credentialStore: credentials);

            viewModel.SpotifyClientId = "   ";
            viewModel.SpotifyClientSecret = string.Empty;

            viewModel.TestAndSaveSpotifyCommand.Execute(null);
            await WaitUntilIdleAsync(viewModel);

            // Leere Felder zu speichern hieße, eine Verknüpfung zu melden, die es nicht gibt.
            Assert.Equal(0, credentials.SaveCallCount);
            Assert.False(viewModel.IsSpotifyLinked);
            Assert.NotEmpty(viewModel.SpotifyStatus);
        }

        [Fact]
        public async Task TestAndSaveSpotify_WithCredentials_StoresThemAndClearsTheFields()
        {
            FakeSpotifyCredentialStore credentials = new();
            OnlineSettingsViewModel viewModel = BuildViewModel(credentialStore: credentials);

            viewModel.SpotifyClientId = $"  {ClientId}  ";
            viewModel.SpotifyClientSecret = $"  {ClientSecret}  ";

            viewModel.TestAndSaveSpotifyCommand.Execute(null);
            await WaitUntilIdleAsync(viewModel);

            Assert.Equal(1, credentials.SaveCallCount);
            Assert.True(viewModel.IsSpotifyLinked);

            // Die Felder werden geleert: Ein Geheimnis, das nach dem Speichern weiter im
            // Formular steht, ist unnötig sichtbar.
            Assert.Empty(viewModel.SpotifyClientId);
            Assert.Empty(viewModel.SpotifyClientSecret);

            (string clientId, string clientSecret)? saved =
                await credentials.GetAsync(TestContext.Current.CancellationToken);
            _ = Assert.NotNull(saved);

            // Abgeschnittene Leerzeichen: Sie stammen aus dem Einfügen aus der Zwischenablage
            // und würden die Anmeldung sonst scheitern lassen.
            Assert.Equal(ClientId, saved.Value.clientId);
            Assert.Equal(ClientSecret, saved.Value.clientSecret);
        }

        [Fact]
        public async Task TestAndSaveSpotify_WhenStoringFails_ShowsTheErrorInsteadOfThrowing()
        {
            FakeSpotifyCredentialStore credentials = new() { FailSaveWith = "Verschlüsselung nicht verfügbar" };
            OnlineSettingsViewModel viewModel = BuildViewModel(credentialStore: credentials);

            viewModel.SpotifyClientId = ClientId;
            viewModel.SpotifyClientSecret = ClientSecret;

            viewModel.TestAndSaveSpotifyCommand.Execute(null);
            await WaitUntilIdleAsync(viewModel);

            // Der Befehl darf nicht reißen — der Anwender bekommt den Fehler in der
            // Statuszeile und kann es erneut versuchen.
            Assert.False(viewModel.IsSpotifyLinked);
            Assert.NotEmpty(viewModel.SpotifyStatus);
            Assert.False(viewModel.IsTestingCredentials);
        }

        [Fact]
        public async Task RemoveSpotify_ClearsTheStoredCredentials()
        {
            FakeSpotifyCredentialStore credentials = new();
            await credentials.SaveAsync(ClientId, ClientSecret, TestContext.Current.CancellationToken);

            OnlineSettingsViewModel viewModel = BuildViewModel(credentialStore: credentials);
            viewModel.LoadSpotifyStatus();

            viewModel.RemoveSpotifyCommand.Execute(null);
            await WaitUntilClearedAsync(credentials);

            Assert.Equal(1, credentials.ClearCallCount);
            Assert.False(viewModel.IsSpotifyLinked);
        }

        [Fact]
        public void LoadFrom_TakesTheProviderWithoutReportingAnEdit()
        {
            int edits = 0;
            OnlineSettingsViewModel viewModel = BuildViewModel(onUserEdit: () => edits++);

            viewModel.LoadFrom(new AppSettings { ActiveProvider = ProviderType.AppleMusic });

            // Das Laden ist keine Eingabe des Anwenders. Zählte es als solche, stünde der
            // Speichern-Knopf nach jedem Öffnen der Einstellungen auf „geändert".
            Assert.Equal(ProviderType.AppleMusic, viewModel.ActiveProvider);
            Assert.Equal(0, edits);
        }

        [Fact]
        public void WriteTo_PassesTheProviderIntoTheSettings()
        {
            OnlineSettingsViewModel viewModel = BuildViewModel();
            viewModel.ActiveProvider = ProviderType.AppleMusic;

            AppSettings settings = new();
            viewModel.WriteTo(settings);

            Assert.Equal(ProviderType.AppleMusic, settings.ActiveProvider);
        }

        [Fact]
        public void ActiveProvider_ChangedByTheUser_IsReportedAsEdit()
        {
            int edits = 0;
            OnlineSettingsViewModel viewModel = BuildViewModel(onUserEdit: () => edits++);

            // Der Ausgangswert ist AppleMusic — gewechselt wird deshalb auf Spotify.
            viewModel.ActiveProvider = ProviderType.Spotify;

            Assert.Equal(1, edits);
        }

        [Fact]
        public void ActiveProvider_SetToTheSameValue_IsNotReportedAsEdit()
        {
            int edits = 0;
            OnlineSettingsViewModel viewModel = BuildViewModel(onUserEdit: () => edits++);
            ProviderType unchanged = viewModel.ActiveProvider;

            viewModel.ActiveProvider = unchanged;

            // Ein Auswahlfeld meldet auch, wenn dieselbe Zeile erneut angeklickt wird. Zählte
            // das als Änderung, stünde der Speichern-Knopf ohne Grund auf „geändert".
            Assert.Equal(0, edits);
        }

        /// <summary>
        /// Die Befehle arbeiten nebenher und liefern keinen Task. Gewartet wird auf das Ende
        /// des Vorgangs, nicht auf eine geratene Zeitspanne.
        /// </summary>
        private static async Task WaitUntilIdleAsync(OnlineSettingsViewModel viewModel)
        {
            for (int attempt = 0; attempt < 200 && viewModel.IsTestingCredentials; attempt++)
            {
                await Task.Yield();
            }

            await Task.Yield();
        }

        private static async Task WaitUntilClearedAsync(FakeSpotifyCredentialStore credentials)
        {
            for (int attempt = 0; attempt < 200 && credentials.ClearCallCount == 0; attempt++)
            {
                await Task.Yield();
            }
        }

        private static OnlineSettingsViewModel BuildViewModel(
            FakeConnectionTestCoordinator? coordinator = null,
            FakeSpotifyCredentialStore? credentialStore = null,
            System.Action? onUserEdit = null)
        {
            FakeSpotifyCredentialStore store = credentialStore ?? new FakeSpotifyCredentialStore();

            return new OnlineSettingsViewModel(
                coordinator ?? new FakeConnectionTestCoordinator(),
                store,
                new FakeSpotifyOptionsProvider(store),
                onUserEdit ?? (() => { }));
        }
    }
}
