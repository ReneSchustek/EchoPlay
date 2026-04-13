using EchoPlay.Spotify.Dtos;
using EchoPlay.Spotify.Tests.Fakes;
using EchoPlay.Spotify.Tests.TestData;
using Xunit;

namespace EchoPlay.Spotify.Tests.Search
{
    /// <summary>
    /// Tests für die technische Spotify-Künstlersuche.
    /// Diese Tests prüfen ausschließlich den korrekten Datenfluss auf API-Ebene ohne Netzwerkzugriffe.
    /// </summary>
    public sealed class SpotifyArtistSearchTests
    {
        /// <summary>
        /// Stellt sicher, dass ein bekannter Hörspiel-Künstler über die Suche gefunden wird.
        /// </summary>
        [Fact]
        public async Task SearchArtists_KnownArtist_ReturnsResult()
        {
            // ARRANGE
            // Der Fake-Client stellt eine kontrollierte Testumgebung bereit.
            FakeSpotifyApiClient apiClient = new(artists: [SpotifyTestData.DieDreiFragezeichen]);

            // ACT
            IReadOnlyList<SpotifyArtistDto> result = await apiClient.SearchArtistsAsync("Die drei ???", 10);

            // ASSERT
            // Es wird genau ein Treffer erwartet.
            _ = Assert.Single(result);

            // Der Name des gefundenen Künstlers muss übereinstimmen.
            Assert.Equal("Die drei ???", result[0].Name);
        }
    }
}
