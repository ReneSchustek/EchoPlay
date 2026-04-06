using EchoPlay.AppleMusic.Dtos;
using EchoPlay.Core.Models.Import;
using EchoPlay.Core.Parsing;
using System.Globalization;

namespace EchoPlay.AppleMusic.Mapping
{
    /// <summary>
    /// Transformiert ein iTunes-Album (Collection) in eine einzelne Import-Episode.
    /// Ein Album bei iTunes entspricht einer Hörspielfolge – die einzelnen Tracks
    /// innerhalb des Albums sind Kapitel, keine eigenständigen Folgen.
    /// </summary>
    public static class AppleMusicEpisodeMapper
    {
        /// <summary>
        /// Erstellt eine Import-Episode aus einem iTunes-Album und seinen Tracks.
        /// Die Tracks werden zu einer Gesamtdauer aufsummiert.
        /// </summary>
        /// <param name="album">Das iTunes-Album (Collection).</param>
        /// <param name="tracks">Die Tracks des Albums – nur für die Dauerberechnung.</param>
        /// <param name="orderIndex">Fachliche Sortierposition innerhalb der Serie.</param>
        /// <returns>Eine Import-Episode die das gesamte Album repräsentiert.</returns>
        public static ImportEpisode MapAlbumToEpisode(ITunesCollectionDto album, IReadOnlyList<ITunesTrackDto> tracks, int orderIndex)
        {
            // Gesamtdauer: Summe aller Track-Dauern
            TimeSpan totalDuration = TimeSpan.Zero;
            foreach (ITunesTrackDto track in tracks)
            {
                totalDuration += TimeSpan.FromMilliseconds(track.TrackTimeMillis ?? 0);
            }

            return new ImportEpisode
            {
                // Collection-ID statt Track-ID – ein Album = eine Folge
                SourceEpisodeId = album.CollectionId.ToString(CultureInfo.InvariantCulture),
                Title           = album.CollectionName,
                EpisodeNumber   = EpisodeNumberParser.Extract(album.CollectionName),
                ReleaseDate     = TryParseReleaseDate(album.ReleaseDate),
                Duration        = totalDuration,
                OrderIndex      = orderIndex,
                ProviderUrl     = $"https://music.apple.com/de/album/{album.CollectionId}",
                // iTunes liefert 100px-Thumbnails – für höhere Auflösung "100x100" durch "600x600" ersetzen
                CoverImageUrl   = album.ArtworkUrl100?.Replace("100x100", "600x600", StringComparison.Ordinal)
            };
        }

        /// <summary>
        /// Versucht, das Veröffentlichungsdatum aus dem ISO-8601-String zu parsen.
        /// </summary>
        private static DateTime? TryParseReleaseDate(string? releaseDate)
        {
            if (string.IsNullOrWhiteSpace(releaseDate))
            {
                return null;
            }

            if (DateTime.TryParse(releaseDate, out DateTime parsed))
            {
                return parsed;
            }

            return null;
        }
    }
}
