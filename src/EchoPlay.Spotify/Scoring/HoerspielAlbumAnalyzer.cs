using EchoPlay.Core.Scoring;
using EchoPlay.Spotify.Dtos;

namespace EchoPlay.Spotify.Scoring
{
    /// <summary>
    /// Analysiert Spotify-Alben auf typische Hörspiel-Merkmale.
    /// Delegiert die eigentliche Heuristik an <see cref="HoerspielAlbumHeuristic"/> aus Core
    /// und übernimmt die Extraktion der Trackdauern aus Spotify-DTOs.
    /// </summary>
    internal static class HoerspielAlbumAnalyzer
    {
        /// <summary>
        /// Prüft, ob eine Liste von Spotify-Tracks die typischen Merkmale eines Hörspiel-Albums aufweist.
        /// </summary>
        /// <param name="tracks">Die Tracks des Albums.</param>
        /// <returns><c>true</c>, wenn die Tracks einem Hörspiel-Muster entsprechen.</returns>
        public static bool LooksLikeHoerspiel(IReadOnlyList<SpotifyTrackDto> tracks)
        {
            List<TimeSpan> durations = new(tracks.Count);

            foreach (SpotifyTrackDto track in tracks)
            {
                durations.Add(track.Duration);
            }

            return HoerspielAlbumHeuristic.LooksLikeHoerspiel(durations);
        }
    }
}
