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
        private const string ValidAppleAlbumId = "1001105149";

        private static EpisodeTileViewModel Build(
            string? spotifyAlbumId,
            bool hasLocalTrack,
            string? appleMusicAlbumId = null) => new(
            episodeId: Guid.NewGuid(),
            episodeNumber: 1,
            title: "Die Insel der Abenteuer",
            totalDuration: TimeSpan.FromMinutes(41),
            playbackStatus: PlaybackStatus.NotStarted,
            releaseDate: null,
            playEpisode: () => { },
            spotifyAlbumId: spotifyAlbumId,
            hasLocalTrack: hasLocalTrack,
            appleMusicAlbumId: appleMusicAlbumId);

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

        [Fact]
        public void OpenInAppleMusic_NoLocalTrackAndAlbumKnown_IsVisible()
        {
            // Der reale Bestand trägt ausschließlich Apple-IDs – ohne diese Aktion bliebe
            // das Kontextmenü dort leer.
            EpisodeTileViewModel tile = Build(null, hasLocalTrack: false, appleMusicAlbumId: ValidAppleAlbumId);

            Assert.Equal(Visibility.Visible, tile.OpenInAppleMusicVisibility);
            Assert.Equal(Visibility.Collapsed, tile.OpenInSpotifyVisibility);
        }

        [Fact]
        public void OpenInAppleMusic_LocalTrackPresent_IsHidden()
        {
            EpisodeTileViewModel tile = Build(null, hasLocalTrack: true, appleMusicAlbumId: ValidAppleAlbumId);

            Assert.Equal(Visibility.Collapsed, tile.OpenInAppleMusicVisibility);
        }

        [Fact]
        public void OpenInAppleMusic_WithoutAlbumId_DoesNothing()
        {
            EpisodeTileViewModel tile = Build(null, hasLocalTrack: false);

            Assert.False(tile.OpenInAppleMusic());
        }

        [Fact]
        public void ProviderSeparator_HiddenWhenNoProviderActionAvailable()
        {
            // Ein Trennstrich ohne Einträge darunter sieht nach kaputtem Menü aus.
            EpisodeTileViewModel tile = Build(null, hasLocalTrack: false);

            Assert.Equal(Visibility.Collapsed, tile.ProviderActionsSeparatorVisibility);
        }

        [Fact]
        public void ProviderSeparator_VisibleWhenAnyProviderActionAvailable()
        {
            EpisodeTileViewModel tile = Build(null, hasLocalTrack: false, appleMusicAlbumId: ValidAppleAlbumId);

            Assert.Equal(Visibility.Visible, tile.ProviderActionsSeparatorVisibility);
        }
    }
}
