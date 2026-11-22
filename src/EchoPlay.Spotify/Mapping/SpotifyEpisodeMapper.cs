using EchoPlay.Core.Http;
using EchoPlay.Core.Models.Import;
using EchoPlay.Spotify.Dtos;

namespace EchoPlay.Spotify.Mapping
{
    /// <summary>
    /// Übersetzt ein Spotify-Album in eine einzelne Import-Episode.
    /// Ein Album bei Spotify entspricht einer Hörspielfolge, die einzelnen Tracks
    /// innerhalb des Albums sind Kapitel, keine eigenständigen Folgen.
    /// </summary>
    internal sealed class SpotifyEpisodeMapper
    {
        /// <summary>
        /// Erstellt eine einzelne Import-Episode aus einem Spotify-Album.
        /// Die Tracks werden zu einer Gesamtdauer aufsummiert.
        /// </summary>
        /// <param name="album">Das Spotify-Album.</param>
        /// <param name="tracks">Die Tracks des Albums, nur für die Dauerberechnung.</param>
        /// <param name="orderIndex">Sortierposition innerhalb der Serie.</param>
        /// <returns>Eine Import-Episode, die das gesamte Album darstellt.</returns>
        public static ImportEpisode MapAlbumToEpisode(SpotifyAlbumDto album, IReadOnlyList<SpotifyTrackDto> tracks, int orderIndex)
        {
            ArgumentNullException.ThrowIfNull(album);
            ArgumentNullException.ThrowIfNull(tracks);

            // Album-ID statt Track-ID, denn ein Album = eine Folge
            ImportEpisodeDraft draft = new()
            {
                SourceEpisodeId = album.SpotifyAlbumId,
                Title = album.Title,
                ReleaseDate = album.ReleaseDate,
                OrderIndex = orderIndex,
                ProviderUrl = WebAddress.TryParse($"https://open.spotify.com/album/{Uri.EscapeDataString(album.SpotifyAlbumId)}"),
                CoverImageUrl = WebAddress.TryParse(album.ImageUrl),
                Source = "Spotify",
            };
            return ImportEpisodeFactory.Create(draft, tracks.Select(track => track.Duration));
        }
    }
}
