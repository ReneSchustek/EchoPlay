using EchoPlay.App.ViewModels;
using System;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Tests für die Deep-Link-URL-Generierung in <see cref="OnlineEpisodeCardViewModel"/>.
    /// Prüft Direktlinks mit Album-ID und Suchlinks als Fallback.
    /// </summary>
    public sealed class OnlineEpisodeCardViewModelDeepLinkTests
    {
        [Fact]
        public void BuildAppleMusicUrl_WithAlbumId_ReturnsDirectLink()
        {
            OnlineEpisodeCardViewModel sut = new(
                episodeId: Guid.NewGuid(),
                episodeNumber: 1,
                title: "Testfolge",
                appleMusicAlbumId: "1234567890");

            string url = sut.BuildAppleMusicUrl();

            Assert.Equal("https://music.apple.com/de/album/1234567890", url);
        }

        [Fact]
        public void BuildAppleMusicUrl_WithoutAlbumId_ReturnsSearchLink()
        {
            OnlineEpisodeCardViewModel sut = new(
                episodeId: Guid.NewGuid(),
                episodeNumber: 1,
                title: "Die drei ??? und der Superpapagei");

            string url = sut.BuildAppleMusicUrl();

            Assert.StartsWith("https://music.apple.com/de/search?term=", url);
            Assert.Contains("Superpapagei", url);
        }

        [Fact]
        public void BuildSpotifyUrl_WithAlbumId_ReturnsDirectLink()
        {
            OnlineEpisodeCardViewModel sut = new(
                episodeId: Guid.NewGuid(),
                episodeNumber: 1,
                title: "Testfolge",
                spotifyAlbumId: "abc123xyz");

            string url = sut.BuildSpotifyUrl();

            Assert.Equal("https://open.spotify.com/album/abc123xyz", url);
        }

        [Fact]
        public void BuildSpotifyUrl_WithoutAlbumId_ReturnsSearchLink()
        {
            OnlineEpisodeCardViewModel sut = new(
                episodeId: Guid.NewGuid(),
                episodeNumber: 1,
                title: "TKKG - Das leere Grab im Moor");

            string url = sut.BuildSpotifyUrl();

            Assert.StartsWith("https://open.spotify.com/search/", url);
            Assert.Contains("TKKG", url);
        }
    }
}
