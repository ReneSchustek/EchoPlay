using EchoPlay.AppleMusic.Dtos;
using EchoPlay.Core.Http;
using EchoPlay.Core.Models;
using System.Globalization;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Wertet die Alben eines Künstlers bei iTunes aus: Neuerscheinungen im Zeitfenster,
    /// angekündigte Folgen und die Folgennummern, die lokal noch fehlen.
    /// </summary>
    internal static class ITunesAlbumEvaluation
    {
        /// <summary>
        /// Sammelt die Alben ab <paramref name="cutoffDate"/> bis heute und die angekündigten.
        /// Ankündigungen erscheinen in beiden Listen, in der der Neuerscheinungen mit ihrem
        /// Zukunftsdatum.
        /// </summary>
        /// <param name="albums">Die Alben des Künstlers.</param>
        /// <param name="cutoffDate">Ältestes erlaubtes Veröffentlichungsdatum (UTC).</param>
        /// <param name="today">Der heutige Tag (UTC).</param>
        /// <returns>Neuerscheinungen, neueste zuerst, und Ankündigungen.</returns>
        public static (List<NewReleaseEpisode> NewReleases, List<AnnouncedEpisode> Announced) CollectNewReleases(
            IReadOnlyList<ITunesCollectionDto> albums, DateTime cutoffDate, DateTime today)
        {
            List<NewReleaseEpisode> newReleases = [];
            List<AnnouncedEpisode> announced = [];

            foreach (ITunesCollectionDto album in albums)
            {
                if (!TryParseReleaseDate(album.ReleaseDate, out DateTime releaseDate))
                {
                    continue;
                }

                bool isAnnounced = releaseDate.Date > today;
                if (isAnnounced)
                {
                    announced.Add(new AnnouncedEpisode { Title = album.CollectionName, ReleaseDate = releaseDate });
                }

                if (isAnnounced || releaseDate.Date >= cutoffDate.Date)
                {
                    newReleases.Add(new NewReleaseEpisode
                    {
                        Title = album.CollectionName,
                        EpisodeNumber = OnlineEpisodeChecker.ExtractEpisodeNumber(album.CollectionName),
                        ReleaseDate = releaseDate,
                        CoverUrl = WebAddress.TryParse(album.ArtworkUrl100),
                        CollectionId = album.CollectionId
                    });
                }
            }

            newReleases.Sort((a, b) => b.ReleaseDate.CompareTo(a.ReleaseDate));
            return (newReleases, announced);
        }

        /// <summary>
        /// Liest die Folgennummern aus den Albumnamen und listet jede Nummer nach
        /// <paramref name="localHighest"/> bis zur höchsten beim Anbieter.
        /// </summary>
        /// <param name="albums">Die Alben des Künstlers.</param>
        /// <param name="localHighest">Die höchste lokal vorhandene Nummer.</param>
        /// <param name="today">Der heutige Tag (UTC).</param>
        /// <returns>Höchste Nummer beim Anbieter, angekündigte Folgen und die fehlenden.</returns>
        public static (int OnlineHighest, List<AnnouncedEpisode> Announced, List<MissingOnlineEpisode> Missing) CollectMissing(
            IReadOnlyList<ITunesCollectionDto> albums, int localHighest, DateTime today)
        {
            int onlineHighest = 0;
            List<AnnouncedEpisode> announced = [];
            // Nummer → Albumname für die Fehlende-Folgen-Liste
            Dictionary<int, string> episodesByNumber = [];

            foreach (ITunesCollectionDto album in albums)
            {
                // ExtractEpisodeNumber filtert bereits auf 1–999
                int? episodeNumber = OnlineEpisodeChecker.ExtractEpisodeNumber(album.CollectionName);
                if (!episodeNumber.HasValue)
                {
                    continue;
                }

                if (TryParseReleaseDate(album.ReleaseDate, out DateTime releaseDate) && releaseDate > today)
                {
                    announced.Add(new AnnouncedEpisode { Title = album.CollectionName, ReleaseDate = releaseDate });
                }

                _ = episodesByNumber.TryAdd(episodeNumber.Value, album.CollectionName);
                onlineHighest = Math.Max(onlineHighest, episodeNumber.Value);
            }

            return (onlineHighest, announced, ListMissing(localHighest, onlineHighest, episodesByNumber));
        }

        /// <summary>
        /// Jede Nummer zwischen der lokalen und der höchsten beim Anbieter; ohne passendes Album
        /// steht ein erzeugter Titel da, damit die Lücke trotzdem sichtbar ist.
        /// </summary>
        private static List<MissingOnlineEpisode> ListMissing(int localHighest, int onlineHighest, Dictionary<int, string> episodesByNumber)
        {
            List<MissingOnlineEpisode> missing = [];
            for (int n = localHighest + 1; n <= onlineHighest; n++)
            {
                missing.Add(new MissingOnlineEpisode
                {
                    EpisodeNumber = n,
                    AlbumTitle = episodesByNumber.TryGetValue(n, out string? name)
                        ? name
                        : FormattableString.Invariant($"Folge {n:D3}")
                });
            }

            return missing;
        }

        /// <summary>
        /// Versucht ein iTunes-Releasedatum zu parsen.
        /// iTunes liefert Daten im ISO-8601-Format (z.B. "2026-04-15T07:00:00Z").
        /// </summary>
        /// <param name="dateString">Der Rohwert von iTunes, erwartet im ISO-8601-Format.</param>
        /// <param name="result">Das geparste Datum; bei Misserfolg <c>default</c>.</param>
        private static bool TryParseReleaseDate(string? dateString, out DateTime result)
        {
            result = default;

            if (string.IsNullOrWhiteSpace(dateString))
            {
                return false;
            }

            return DateTime.TryParse(dateString, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result);
        }
    }
}
