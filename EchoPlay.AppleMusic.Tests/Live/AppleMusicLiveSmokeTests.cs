using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Dtos;

namespace EchoPlay.AppleMusic.Tests.Live
{
    /// <summary>
    /// Technischer Smoke-Test für die iTunes Search API.
    ///
    /// Die Tests sind bewusst deaktiviert, da:
    /// - sie echte Netzwerkzugriffe benötigen
    /// - Firewall, Proxy oder VPN den Zugriff verhindern können
    /// - dies keinen Rückschluss auf die Codequalität zulässt
    ///
    /// Die Tests dienen ausschließlich der manuellen Verifikation
    /// in einer kontrollierten Umgebung.
    ///
    /// Im Gegensatz zu Spotify sind keine Credentials erforderlich,
    /// da die iTunes Search API öffentlich und kostenfrei ist.
    /// </summary>
    public sealed class AppleMusicLiveSmokeTests : IClassFixture<AppleMusicLiveFixture>
    {
        private readonly IAppleMusicSearchClient _searchClient;

        /// <summary>
        /// Initialisiert die Testklasse mit der gemeinsamen Live-Fixture.
        /// </summary>
        /// <param name="fixture">Die geteilte Fixture mit konfiguriertem Search-Client.</param>
        public AppleMusicLiveSmokeTests(AppleMusicLiveFixture fixture)
        {
            _searchClient = fixture.SearchClient;
        }

        /// <summary>
        /// Führt eine reale Künstler-Suche gegen die iTunes Search API aus.
        ///
        /// Zum Aktivieren den Skip-Parameter temporär entfernen.
        /// </summary>

        //[Fact]
        [Fact(Skip = "Manuell ausführen – benötigt Internetzugang")]
        public async Task ITunesApi_IsReachable_AndReturnsArtists()
        {
            // ACT
            ITunesResponseDto<ITunesArtistDto> response =
                await _searchClient.SearchArtistsAsync("Die drei ???", 3);

            // ASSERT
            Assert.True(response.ResultCount > 0);
            Assert.NotEmpty(response.Results);
            Assert.True(response.Results[0].ArtistId > 0);
            Assert.False(string.IsNullOrWhiteSpace(response.Results[0].ArtistName));
        }

        /// <summary>
        /// Prüft, ob Alben eines bekannten Künstlers geladen werden können.
        ///
        /// Zum Aktivieren den Skip-Parameter temporär entfernen.
        /// </summary>
         
        //[Fact]
        [Fact(Skip = "Manuell ausführen – benötigt Internetzugang")]
        public async Task ITunesApi_LookupAlbums_ReturnsAlbums()
        {
            // ARRANGE – Künstler suchen, um eine gültige Artist-ID zu erhalten
            ITunesResponseDto<ITunesArtistDto> artists =
                await _searchClient.SearchArtistsAsync("Die drei ???", 1);

            Assert.NotEmpty(artists.Results);
            long artistId = artists.Results[0].ArtistId;

            // ACT
            ITunesResponseDto<ITunesCollectionDto> albums =
                await _searchClient.LookupAlbumsAsync(artistId);

            // ASSERT – Mindestens ein Album (neben dem Artist-Eintrag)
            List<ITunesCollectionDto> collections = albums.Results
                .Where(r => string.Equals(r.WrapperType, "collection", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.NotEmpty(collections);
            Assert.True(collections[0].CollectionId > 0);
            Assert.False(string.IsNullOrWhiteSpace(collections[0].CollectionName));
        }

        /// <summary>
        /// Prüft, ob Tracks eines bekannten Albums geladen werden können.
        ///
        /// Zum Aktivieren den Skip-Parameter temporär entfernen.
        /// </summary>
        
        //[Fact]
        [Fact(Skip = "Manuell ausführen – benötigt Internetzugang")]
        public async Task ITunesApi_LookupTracks_ReturnsTracks()
        {
            // ARRANGE – Künstler und Album suchen
            ITunesResponseDto<ITunesArtistDto> artists =
                await _searchClient.SearchArtistsAsync("Die drei ???", 1);

            Assert.NotEmpty(artists.Results);

            ITunesResponseDto<ITunesCollectionDto> albums =
                await _searchClient.LookupAlbumsAsync(artists.Results[0].ArtistId);

            ITunesCollectionDto? firstAlbum = albums.Results
                .FirstOrDefault(r => string.Equals(r.WrapperType, "collection", StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(firstAlbum);

            // ACT
            ITunesResponseDto<ITunesTrackDto> tracks =
                await _searchClient.LookupTracksAsync(firstAlbum.CollectionId);

            // ASSERT
            List<ITunesTrackDto> trackList = tracks.Results
                .Where(r => string.Equals(r.WrapperType, "track", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.NotEmpty(trackList);
            Assert.True(trackList[0].TrackId > 0);
            Assert.False(string.IsNullOrWhiteSpace(trackList[0].TrackName));
            Assert.True(trackList[0].TrackTimeMillis > 0);
        }
    }
}
