using EchoPlay.App.Models;
using EchoPlay.App.ViewModels;
using Microsoft.UI.Xaml;
using System;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Sichtbarkeit der Aktion „In Spotify öffnen" auf der Folgen-Kachel.
    /// Die Aktion selbst wird hier nicht ausgelöst — sie öffnet einen externen Link.
    /// </summary>
    public sealed class EpisodeTileSpotifyTests
    {
        private const string ValidAlbumId = "4aawyAB9vmqN3uQ7FjRGTy";

        private static EpisodeTileViewModel Build(string? spotifyAlbumId, bool hasLocalTrack) => new(
            episodeId: Guid.NewGuid(),
            episodeNumber: 1,
            title: "Die Insel der Abenteuer",
            totalDuration: TimeSpan.FromMinutes(41),
            playbackStatus: PlaybackStatus.NotStarted,
            releaseDate: null,
            playEpisode: () => { },
            spotifyAlbumId: spotifyAlbumId,
            hasLocalTrack: hasLocalTrack);

        [Fact]
        public void OpenInSpotify_NoLocalTrackAndAlbumKnown_IsVisible()
        {
            EpisodeTileViewModel tile = Build(ValidAlbumId, hasLocalTrack: false);

            Assert.Equal(Visibility.Visible, tile.OpenInSpotifyVisibility);
        }

        [Fact]
        public void OpenInSpotify_LocalTrackPresent_IsHidden()
        {
            // Wo lokal abgespielt werden kann, ist der Umweg über Spotify kein Gewinn.
            EpisodeTileViewModel tile = Build(ValidAlbumId, hasLocalTrack: true);

            Assert.Equal(Visibility.Collapsed, tile.OpenInSpotifyVisibility);
        }

        [Fact]
        public void OpenInSpotify_WithoutAlbumId_IsHidden()
        {
            EpisodeTileViewModel tile = Build(spotifyAlbumId: null, hasLocalTrack: false);

            Assert.Equal(Visibility.Collapsed, tile.OpenInSpotifyVisibility);
        }

        [Fact]
        public void OpenInSpotify_InvalidAlbumId_IsHidden()
        {
            // Lieber gar keine Aktion als eine, die auf einer kaputten URL landet.
            EpisodeTileViewModel tile = Build("kaputt", hasLocalTrack: false);

            Assert.Equal(Visibility.Collapsed, tile.OpenInSpotifyVisibility);
        }

        [Fact]
        public void OpenInSpotify_WithoutAlbumId_DoesNothing()
        {
            // Ohne gültige ID darf kein Link gebaut und nichts geöffnet werden.
            EpisodeTileViewModel tile = Build(spotifyAlbumId: null, hasLocalTrack: false);

            Assert.False(tile.OpenInSpotify());
        }
    }
}
