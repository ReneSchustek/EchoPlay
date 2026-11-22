using EchoPlay.Core.Http;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.Core.Models.Import;
using System.Globalization;

namespace EchoPlay.AppleMusic.Mapping
{
    /// <summary>
    /// Transformiert ein iTunes-Album (Collection) in eine einzelne Import-Episode.
    /// Ein Album bei iTunes entspricht einer Hörspielfolge, die einzelnen Tracks
    /// innerhalb des Albums sind Kapitel, keine eigenständigen Folgen.
    /// </summary>
    public static class AppleMusicEpisodeMapper
    {
        /// <summary>
        /// Erstellt eine Import-Episode aus einem iTunes-Album und seinen Tracks.
        /// Die Tracks werden zu einer Gesamtdauer aufsummiert.
        /// </summary>
        /// <param name="album">Das iTunes-Album (Collection).</param>
        /// <param name="tracks">Die Tracks des Albums, nur für die Dauerberechnung.</param>
        /// <param name="orderIndex">Fachliche Sortierposition innerhalb der Serie.</param>
        /// <returns>Eine Import-Episode, die das gesamte Album darstellt.</returns>
        public static ImportEpisode MapAlbumToEpisode(ITunesCollectionDto album, IReadOnlyList<ITunesTrackDto> tracks, int orderIndex)
        {
            ArgumentNullException.ThrowIfNull(album);
            ArgumentNullException.ThrowIfNull(tracks);

            // Collection-ID statt Track-ID, weil ein Album eine Folge ist. iTunes liefert nur
            // eine 100er-Vorschau; die Größe im Pfad lässt sich gegen das große Bild tauschen.
            return ImportEpisodeFactory.Create(
                sourceEpisodeId: album.CollectionId.ToString(CultureInfo.InvariantCulture),
                title: album.CollectionName,
                releaseDate: TryParseReleaseDate(album.ReleaseDate),
                trackDurations: tracks.Select(track => TimeSpan.FromMilliseconds(track.TrackTimeMillis ?? 0)),
                orderIndex: orderIndex,
                providerUrl: WebAddress.TryParse($"https://music.apple.com/de/album/{album.CollectionId}"),
                coverImageUrl: AppleMusicArtworkUrl.WithSize(WebAddress.TryParse(album.ArtworkUrl100), AppleMusicArtworkUrl.EpisodeCoverSize),
                source: "AppleMusic");
        }

        /// <summary>
        /// Versucht, das Veröffentlichungsdatum aus dem ISO-8601-String zu parsen.
        /// </summary>
        private static DateTime? TryParseReleaseDate(string? releaseDate)
            => DateTime.TryParse(releaseDate, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed)
                ? parsed
                : null;
    }
}
