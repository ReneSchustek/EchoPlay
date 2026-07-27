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
            string? appleMusicAlbumId = null,
            string? seriesTitle = "Benjamin Blümchen") => new(
            episodeId: Guid.NewGuid(),
            episodeNumber: 1,
            title: "Die Insel der Abenteuer",
            totalDuration: TimeSpan.FromMinutes(41),
            playbackStatus: PlaybackStatus.NotStarted,
            releaseDate: null,
            playEpisode: () => { },
            spotifyAlbumId: spotifyAlbumId,
            hasLocalTrack: hasLocalTrack,
            appleMusicAlbumId: appleMusicAlbumId,
            seriesTitle: seriesTitle);

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
        public void OpenInSpotify_WithoutAlbumId_StaysAvailableViaSearch()
        {
            // Spotify soll unabhängig davon nutzbar sein, über welchen Anbieter importiert
            // wurde — ohne Album-ID greift die Suche nach Serie und Folge.
            EpisodeTileViewModel tile = Build(spotifyAlbumId: null, hasLocalTrack: false);

            Assert.Equal(Visibility.Visible, tile.OpenInSpotifyVisibility);
        }

        [Fact]
        public void OpenInSpotify_InvalidAlbumId_FallsBackToSearch()
        {
            // Eine kaputte ID darf nicht in der URL landen, die Aktion aber trotzdem tragen.
            EpisodeTileViewModel tile = Build("kaputt", hasLocalTrack: false);

            Assert.Equal(Visibility.Visible, tile.OpenInSpotifyVisibility);
        }

        [Fact]
        public void OpenInSpotify_WithoutAnyIdentifyingText_IsHidden()
        {
            // Ohne Serien- und Folgentitel gäbe es nichts zu suchen.
            EpisodeTileViewModel tile = new(
                episodeId: Guid.NewGuid(),
                episodeNumber: null,
                title: string.Empty,
                totalDuration: null,
                playbackStatus: PlaybackStatus.NotStarted,
                releaseDate: null,
                playEpisode: () => { },
                hasLocalTrack: false,
                seriesTitle: null);

            Assert.Equal(Visibility.Collapsed, tile.OpenInSpotifyVisibility);
            Assert.False(tile.OpenInSpotify());
        }

        [Fact]
        public void OpenInAppleMusic_NoLocalTrackAndAlbumKnown_IsVisible()
        {
            // Der reale Bestand trägt ausschließlich Apple-IDs – ohne diese Aktion bliebe
            // das Kontextmenü dort leer.
            EpisodeTileViewModel tile = Build(null, hasLocalTrack: false, appleMusicAlbumId: ValidAppleAlbumId);

            Assert.Equal(Visibility.Visible, tile.OpenInAppleMusicVisibility);
            // Spotify steht daneben zur Verfügung – ohne Album-ID über die Suche.
            Assert.Equal(Visibility.Visible, tile.OpenInSpotifyVisibility);
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
            // Lokale Folge ohne Anbieter-IDs: beide Aktionen entfallen.
            EpisodeTileViewModel tile = Build(null, hasLocalTrack: true);

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
