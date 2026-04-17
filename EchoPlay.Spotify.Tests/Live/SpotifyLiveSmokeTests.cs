using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.Dtos;
using Xunit;

namespace EchoPlay.Spotify.Tests.Live
{
    /// <summary>
    /// Technischer Smoke-Test fuer die Spotify-Web-API.
    ///
    /// Die Tests sind bewusst deaktiviert, da:
    /// - sie echte Netzwerkzugriffe benoetigen
    /// - Firewall, Proxy oder VPN den Zugriff verhindern koennen
    /// - dies keinen Rueckschluss auf die Codequalitaet zulaesst
    ///
    /// Die Tests dienen ausschliesslich der manuellen Verifikation
    /// in einer kontrollierten Umgebung.
    ///
    /// Credentials werden aus User Secrets geladen:
    ///   dotnet user-secrets set "Spotify:ClientId" "DEIN_CLIENT_ID" --project EchoPlay.Spotify.Tests
    ///   dotnet user-secrets set "Spotify:ClientSecret" "DEIN_CLIENT_SECRET" --project EchoPlay.Spotify.Tests
    /// </summary>
    public sealed class SpotifyLiveSmokeTests : IClassFixture<SpotifyLiveFixture>
    {
        private readonly ISpotifyApiClient _apiClient;

        /// <summary>
        /// Initialisiert die Testklasse mit der gemeinsamen Live-Fixture.
        /// </summary>
        /// <param name="fixture">Die geteilte Fixture mit konfiguriertem API-Client.</param>
        public SpotifyLiveSmokeTests(SpotifyLiveFixture fixture)
        {
            ArgumentNullException.ThrowIfNull(fixture);
            _apiClient = fixture.ApiClient;
        }

        /// <summary>
        /// Fuehrt eine reale Kuenstler-Suche gegen Spotify aus.
        ///
        /// Zum Aktivieren den Skip-Parameter temporaer entfernen.
        /// </summary>

        //[Fact]
        [Fact(Skip = "Manuell ausfuehren – benoetigt Internetzugang und gueltige Spotify-Credentials")]
        public async Task SpotifyApi_IsReachable_AndReturnsArtists()
        {
            // ACT
            IReadOnlyList<SpotifyArtistDto> artists =
                await _apiClient.SearchArtistsAsync("Die drei ???", 3);

            // ASSERT
            Assert.NotEmpty(artists);
            Assert.False(string.IsNullOrWhiteSpace(artists[0].SpotifyArtistId));
            Assert.False(string.IsNullOrWhiteSpace(artists[0].Name));
        }

        /// <summary>
        /// Prueft, ob Alben eines bekannten Kuenstlers geladen werden koennen.
        ///
        /// Zum Aktivieren den Skip-Parameter temporaer entfernen.
        /// </summary>

        //[Fact]
        [Fact(Skip = "Manuell ausfuehren – benoetigt Internetzugang und gueltige Spotify-Credentials")]
        public async Task SpotifyApi_GetArtistAlbums_ReturnsAlbums()
        {
            // ARRANGE – Kuenstler suchen, um eine gueltige Artist-ID zu erhalten
            IReadOnlyList<SpotifyArtistDto> artists =
                await _apiClient.SearchArtistsAsync("Die drei ???", 1);

            Assert.NotEmpty(artists);
            string artistId = artists[0].SpotifyArtistId;

            // ACT
            IReadOnlyList<SpotifyAlbumDto> albums =
                await _apiClient.GetArtistAlbumsAsync(artistId, 5);

            // ASSERT
            Assert.NotEmpty(albums);
            Assert.False(string.IsNullOrWhiteSpace(albums[0].SpotifyAlbumId));
            Assert.False(string.IsNullOrWhiteSpace(albums[0].Title));
        }

        /// <summary>
        /// Prueft, ob Tracks eines bekannten Albums geladen werden koennen.
        ///
        /// Zum Aktivieren den Skip-Parameter temporaer entfernen.
        /// </summary>

        //[Fact]
        [Fact(Skip = "Manuell ausfuehren – benoetigt Internetzugang und gueltige Spotify-Credentials")]
        public async Task SpotifyApi_GetAlbumTracks_ReturnsTracks()
        {
            // ARRANGE – Kuenstler und Album suchen
            IReadOnlyList<SpotifyArtistDto> artists =
                await _apiClient.SearchArtistsAsync("Die drei ???", 1);

            Assert.NotEmpty(artists);

            IReadOnlyList<SpotifyAlbumDto> albums =
                await _apiClient.GetArtistAlbumsAsync(artists[0].SpotifyArtistId, 1);

            Assert.NotEmpty(albums);
            string albumId = albums[0].SpotifyAlbumId;

            // ACT
            IReadOnlyList<SpotifyTrackDto> tracks =
                await _apiClient.GetAlbumTracksAsync(albumId);

            // ASSERT
            Assert.NotEmpty(tracks);
            Assert.False(string.IsNullOrWhiteSpace(tracks[0].SpotifyTrackId));
            Assert.False(string.IsNullOrWhiteSpace(tracks[0].Title));
            Assert.True(tracks[0].Duration > TimeSpan.Zero);
        }
    }
}
