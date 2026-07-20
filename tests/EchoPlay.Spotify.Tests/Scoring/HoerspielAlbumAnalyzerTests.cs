using EchoPlay.Spotify.Dtos;
using EchoPlay.Spotify.Scoring;

namespace EchoPlay.Spotify.Tests.Scoring
{
    /// <summary>
    /// Tests für die statische Album-Analyse-Logik.
    /// Jeder Test prüft ein klar definiertes Kriterium der Hörspiel-Erkennung anhand der Track-Struktur.
    /// </summary>
    public sealed class HoerspielAlbumAnalyzerTests
    {
        /// <summary>
        /// Ein typisches Hörspiel-Album mit einem langen Track und hoher Durchschnittsdauer wird erkannt.
        /// </summary>
        [Fact]
        public void LooksLikeHoerspiel_SingleLongTrack_ReturnsTrue()
        {
            IReadOnlyList<SpotifyTrackDto> tracks =
            [
                new SpotifyTrackDto
                {
                    SpotifyTrackId = "track-1",
                    Title = "Komplette Folge",
                    Duration = TimeSpan.FromMinutes(45),
                    TrackNumber = 1
                }
            ];

            bool result = HoerspielAlbumAnalyzer.LooksLikeHoerspiel(tracks);

            Assert.True(result);
        }

        /// <summary>
        /// Ein Album mit mehreren Kapiteln, davon mindestens eines lang genug, wird erkannt.
        /// </summary>
        [Fact]
        public void LooksLikeHoerspiel_MultipleChaptersWithLongTrack_ReturnsTrue()
        {
            IReadOnlyList<SpotifyTrackDto> tracks =
            [
                new SpotifyTrackDto
                {
                    SpotifyTrackId = "track-1",
                    Title = "Kapitel 1",
                    Duration = TimeSpan.FromMinutes(12),
                    TrackNumber = 1
                },
                new SpotifyTrackDto
                {
                    SpotifyTrackId = "track-2",
                    Title = "Kapitel 2",
                    Duration = TimeSpan.FromMinutes(8),
                    TrackNumber = 2
                },
                new SpotifyTrackDto
                {
                    SpotifyTrackId = "track-3",
                    Title = "Kapitel 3",
                    Duration = TimeSpan.FromMinutes(10),
                    TrackNumber = 3
                }
            ];

            bool result = HoerspielAlbumAnalyzer.LooksLikeHoerspiel(tracks);

            Assert.True(result);
        }

        /// <summary>
        /// Kurze Tracks wie bei einem Musikalbum werden abgelehnt.
        /// </summary>
        [Fact]
        public void LooksLikeHoerspiel_ShortMusicTracks_ReturnsFalse()
        {
            IReadOnlyList<SpotifyTrackDto> tracks =
            [
                new SpotifyTrackDto
                {
                    SpotifyTrackId = "track-1",
                    Title = "Song 1",
                    Duration = TimeSpan.FromMinutes(3),
                    TrackNumber = 1
                },
                new SpotifyTrackDto
                {
                    SpotifyTrackId = "track-2",
                    Title = "Song 2",
                    Duration = TimeSpan.FromMinutes(4),
                    TrackNumber = 2
                }
            ];

            bool result = HoerspielAlbumAnalyzer.LooksLikeHoerspiel(tracks);

            Assert.False(result);
        }

        /// <summary>
        /// Ein Album mit mehr als 20 Tracks ist untypisch für Hörspiele und wird abgelehnt.
        /// </summary>
        [Fact]
        public void LooksLikeHoerspiel_TooManyTracks_ReturnsFalse()
        {
            List<SpotifyTrackDto> tracks = [];

            for (int i = 1; i <= 21; i++)
            {
                tracks.Add(new SpotifyTrackDto
                {
                    SpotifyTrackId = $"track-{i}",
                    Title = $"Track {i}",
                    Duration = TimeSpan.FromMinutes(10),
                    TrackNumber = i
                });
            }

            bool result = HoerspielAlbumAnalyzer.LooksLikeHoerspiel(tracks);

            Assert.False(result);
        }

        /// <summary>
        /// Eine leere Trackliste wird abgelehnt.
        /// </summary>
        [Fact]
        public void LooksLikeHoerspiel_EmptyTrackList_ReturnsFalse()
        {
            IReadOnlyList<SpotifyTrackDto> tracks = [];

            bool result = HoerspielAlbumAnalyzer.LooksLikeHoerspiel(tracks);

            Assert.False(result);
        }

        /// <summary>
        /// Ein langer Track bei niedriger Durchschnittsdauer reicht nicht aus.
        /// Der Durchschnitt muss ebenfalls über dem Schwellenwert liegen.
        /// </summary>
        [Fact]
        public void LooksLikeHoerspiel_OneLongTrackButLowAverage_ReturnsFalse()
        {
            IReadOnlyList<SpotifyTrackDto> tracks =
            [
                new SpotifyTrackDto
                {
                    SpotifyTrackId = "track-1",
                    Title = "Intro",
                    Duration = TimeSpan.FromMinutes(1),
                    TrackNumber = 1
                },
                new SpotifyTrackDto
                {
                    SpotifyTrackId = "track-2",
                    Title = "Langer Track",
                    Duration = TimeSpan.FromMinutes(11),
                    TrackNumber = 2
                },
                new SpotifyTrackDto
                {
                    SpotifyTrackId = "track-3",
                    Title = "Outro",
                    Duration = TimeSpan.FromMinutes(1),
                    TrackNumber = 3
                }
            ];

            // Durchschnitt: (1+11+1)/3 ≈ 4.33 Min → unter 5 Min Schwelle
            bool result = HoerspielAlbumAnalyzer.LooksLikeHoerspiel(tracks);

            Assert.False(result);
        }
    }
}
