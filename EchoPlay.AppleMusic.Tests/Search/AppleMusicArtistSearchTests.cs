using EchoPlay.AppleMusic.Dtos;
using EchoPlay.AppleMusic.Tests.Fakes;
using EchoPlay.AppleMusic.Tests.TestData;

namespace EchoPlay.AppleMusic.Tests.Search
{
    /// <summary>
    /// Tests für die technische iTunes-Künstlersuche.
    /// Diese Tests prüfen ausschließlich den korrekten Datenfluss auf API-Ebene ohne Netzwerkzugriffe.
    /// </summary>
    public sealed class AppleMusicArtistSearchTests
    {
        /// <summary>
        /// Stellt sicher, dass ein bekannter Hörspiel-Künstler über die Suche gefunden wird.
        /// </summary>
        [Fact]
        public async Task SearchArtists_KnownArtist_ReturnsResult()
        {
            // ARRANGE
            FakeAppleMusicSearchClient searchClient = new(artists: [AppleMusicTestData.DieDreiFragezeichen]);

            // ACT
            ITunesResponseDto<ITunesArtistDto> result = await searchClient.SearchArtistsAsync("Die drei ???");

            // ASSERT
            _ = Assert.Single(result.Results);
            Assert.Equal("Die drei ???", result.Results[0].ArtistName);
        }

        /// <summary>
        /// Stellt sicher, dass ein nicht vorhandener Künstler nicht gefunden wird.
        /// </summary>
        [Fact]
        public async Task SearchArtists_UnknownArtist_ReturnsEmptyResult()
        {
            // ARRANGE
            FakeAppleMusicSearchClient searchClient = new(artists: [AppleMusicTestData.DieDreiFragezeichen]);

            // ACT
            ITunesResponseDto<ITunesArtistDto> result = await searchClient.SearchArtistsAsync("Nicht vorhanden");

            // ASSERT
            Assert.Empty(result.Results);
        }

        /// <summary>
        /// Stellt sicher, dass das Genre des Künstlers korrekt mitgeliefert wird.
        /// </summary>
        [Fact]
        public async Task SearchArtists_KnownArtist_ContainsGenre()
        {
            // ARRANGE
            FakeAppleMusicSearchClient searchClient = new(artists: [AppleMusicTestData.DieDreiFragezeichen]);

            // ACT
            ITunesResponseDto<ITunesArtistDto> result = await searchClient.SearchArtistsAsync("Die drei ???");

            // ASSERT
            Assert.Equal("Hörspiele", result.Results[0].PrimaryGenreName);
        }
    }
}
