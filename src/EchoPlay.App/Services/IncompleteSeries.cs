using EchoPlay.Data.Entities.Library;
using EchoPlay.LocalLibrary.Parsing;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Bestimmt, welche Serien der lokalen Mediathek unvollständig sind: Lücken in der
    /// Nummernfolge oder Folgen nach der letzten lokalen, die der Anbieter schon geliefert hat.
    /// Dieselbe Frage beantwortet der Bericht „Fehlende Folgen"; hier aus den Daten, die die
    /// Mediathek ohnehin lädt, ohne Ordner zu lesen und ohne Netz.
    /// </summary>
    /// <remarks>
    /// Eine lokale Serie kennt nur die Folgen ihrer eigenen Ordner. Neue Folgen stehen bei ihrem
    /// Online-Gegenstück, das über die Künstlerkennung gefunden wird, zuerst Apple Music, dann
    /// Spotify, in derselben Reihenfolge wie <see cref="KnownNewEpisodeLookup"/>.
    /// </remarks>
    internal static class IncompleteSeries
    {
        /// <summary>
        /// Liefert die Kennungen aller lokalen Serien, denen etwas fehlt.
        /// </summary>
        /// <param name="allSeries">Alle Serien, lokale wie online importierte.</param>
        /// <param name="episodes">Die Folgen der lokalen Serien und ihrer Online-Gegenstücke.</param>
        /// <returns>Die Kennungen der unvollständigen lokalen Serien.</returns>
        public static IReadOnlySet<Guid> Find(IReadOnlyList<Series> allSeries, IReadOnlyList<Episode> episodes)
        {
            ArgumentNullException.ThrowIfNull(allSeries);
            ArgumentNullException.ThrowIfNull(episodes);

            ILookup<Guid, Episode> episodesBySeries = episodes.ToLookup(episode => episode.SeriesId);
            HashSet<Guid> incomplete = [];
            foreach (Series series in allSeries.Where(series => series.LocalFolderPath is not null))
            {
                Series? provider = FindProviderSeries(series, allSeries);
                IEnumerable<Episode> providerEpisodes = provider is null ? [] : episodesBySeries[provider.Id];
                if (IsIncomplete(episodesBySeries[series.Id], providerEpisodes))
                {
                    _ = incomplete.Add(series.Id);
                }
            }

            return incomplete;
        }

        /// <summary>
        /// Die Kennungen der Serien, deren Folgen <see cref="Find"/> braucht: die lokalen Serien
        /// und ihre Online-Gegenstücke.
        /// </summary>
        /// <param name="allSeries">Alle Serien.</param>
        /// <returns>Die Kennungen für eine einzige Sammelabfrage der Folgen.</returns>
        public static IReadOnlyList<Guid> SeriesToLoad(IReadOnlyList<Series> allSeries)
        {
            ArgumentNullException.ThrowIfNull(allSeries);

            return allSeries
                .Where(series => series.LocalFolderPath is not null)
                .SelectMany(series => new[] { series, FindProviderSeries(series, allSeries) })
                .OfType<Series>()
                .Select(series => series.Id)
                .Distinct().ToArray();
        }

        /// <summary>
        /// Lücke zwischen der kleinsten und der größten lokalen Nummer, oder eine Folge des
        /// Anbieters nach der größten. Ohne lokale Nummer gibt es kein „danach"; jede Folge des
        /// Anbieters wäre sonst neu.
        /// </summary>
        private static bool IsIncomplete(IEnumerable<Episode> localEpisodes, IEnumerable<Episode> providerEpisodes)
        {
            List<int> numbers = [.. localEpisodes
                .Where(episode => episode.LocalFolderPath is not null && episode.EpisodeNumber is not null)
                .Select(episode => episode.EpisodeNumber!.Value) // nach dem Filter oben nie null
                .Distinct()
                .Order()];
            if (numbers.Count == 0)
            {
                return false;
            }

            // Was beim Anbieter Dateien hat, liegt vor; ist die lokale Serie selbst der Online-Import,
            // stehen ihre lokalen Folgen in derselben Liste.
            int highest = numbers[^1];
            return LocalEpisodeNumbers.FindGaps(numbers).Count > 0
                || providerEpisodes.Any(episode => episode.EpisodeNumber > highest && (episode.LocalTrackCount ?? 0) == 0);
        }

        private static Series? FindProviderSeries(Series localSeries, IReadOnlyList<Series> allSeries)
        {
            Series? byAppleMusic = string.IsNullOrWhiteSpace(localSeries.AppleMusicArtistId)
                ? null
                : allSeries.FirstOrDefault(series => series.IsOnlineImported && series.AppleMusicArtistId == localSeries.AppleMusicArtistId);

            return byAppleMusic ?? (string.IsNullOrWhiteSpace(localSeries.SpotifyArtistId)
                ? null
                : allSeries.FirstOrDefault(series => series.IsOnlineImported && series.SpotifyArtistId == localSeries.SpotifyArtistId));
        }
    }
}
