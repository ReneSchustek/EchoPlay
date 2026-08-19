using EchoPlay.App.Tests.Fakes;
using EchoPlay.App.ViewModels;
using EchoPlay.Data.Entities.Settings;
using System;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft die Umrechnung zwischen dem Auswahlfeld der Einstellungen und dem hinterlegten
    /// Anbieter — und den Hinweis, wenn gespeicherte Zugangsdaten nicht mehr lesbar sind.
    /// </summary>
    /// <remarks>
    /// Die Oberfläche kennt nur Zeichenketten, der Bestand nur den Aufzählungswert. Stimmt
    /// die Umrechnung nicht, wählt der Anwender „Apple Music" und die Suche fragt weiter
    /// bei Spotify — ohne jede Meldung.
    /// </remarks>
    public sealed class OnlineProviderTagTests
    {
        [Theory]
        [InlineData(ProviderType.Spotify, "Spotify")]
        [InlineData(ProviderType.AppleMusic, "AppleMusic")]
        [InlineData(ProviderType.Both, "Both")]
        [InlineData(ProviderType.None, "")]
        public void ActiveProviderTag_FollowsTheChosenProvider(ProviderType provider, string expected)
        {
            OnlineSettingsViewModel sut = BuildViewModel(out _);

            sut.ActiveProvider = provider;

            Assert.Equal(expected, sut.ActiveProviderTag);
        }

        [Theory]
        [InlineData("Spotify", ProviderType.Spotify)]
        [InlineData("AppleMusic", ProviderType.AppleMusic)]
        [InlineData("Both", ProviderType.Both)]
        [InlineData("", ProviderType.None)]
        [InlineData("Unbekannt", ProviderType.None)]
        public void ActiveProviderTag_WhenSet_ChoosesTheMatchingProvider(string tag, ProviderType expected)
        {
            OnlineSettingsViewModel sut = BuildViewModel(out _);

            sut.ActiveProviderTag = tag;

            // Ein unbekannter Wert darf nicht stillschweigend bei Spotify landen — „kein
            // Anbieter" ist die ehrliche Antwort.
            Assert.Equal(expected, sut.ActiveProvider);
        }

        [Fact]
        public void LoadSpotifyStatus_WithoutCredentials_ReportsNotLinked()
        {
            OnlineSettingsViewModel sut = BuildViewModel(out _);

            sut.LoadSpotifyStatus();

            Assert.False(sut.IsSpotifyLinked);
            Assert.NotNull(sut.SpotifyStatus);
        }

        [Fact]
        public async Task LoadSpotifyStatus_AfterUnreadableCredentials_AsksForThemAgain()
        {
            FakeSpotifyCredentialStore store = new() { SimulateCryptographicFailure = true };
            OnlineSettingsViewModel sut = BuildViewModel(out _, store);

            // Der Fehlschlag zeigt sich erst beim Lesen — danach steht das Kennzeichen.
            _ = await store.GetAsync(TestContext.Current.CancellationToken);

            sut.LoadSpotifyStatus();

            // Nach einem Profilwechsel sind die verschlüsselten Angaben wertlos. Stünde
            // weiter „Verknüpft" da, suchte der Anwender den Fehler überall sonst.
            Assert.Contains("entschlüsselt", sut.SpotifyStatus, StringComparison.OrdinalIgnoreCase);
            Assert.False(store.LastLoadFailedDueToCorruption);
        }

        private static OnlineSettingsViewModel BuildViewModel(
            out FakeSpotifyCredentialStore store,
            FakeSpotifyCredentialStore? credentialStore = null)
        {
            store = credentialStore ?? new FakeSpotifyCredentialStore();

            return new OnlineSettingsViewModel(
                new FakeConnectionTestCoordinator(),
                store,
                new FakeSpotifyOptionsProvider(store),
                onUserEdit: () => { });
        }
    }
}
