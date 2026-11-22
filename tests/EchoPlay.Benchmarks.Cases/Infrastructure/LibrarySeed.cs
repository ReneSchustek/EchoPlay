using EchoPlay.Core.Models;
using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;

namespace EchoPlay.Benchmarks.Cases.Infrastructure
{
    /// <summary>
    /// Erzeugt einen Bestand in Größe und Verteilung der Produktivbibliothek (<see cref="LibraryShape"/>),
    /// aus einem festen Startwert und damit auf jedem Rechner gleich.
    /// </summary>
    /// <remarks>
    /// Die Datei liegt im Messprojekt und wird in die Wächtertests verlinkt, damit beide gegen
    /// denselben Bestand laufen. Cover-Bilder fehlen bewusst: Sie machen den Großteil der Datei aus
    /// (5.194 Stück, im Mittel 255 KB), betreffen aber keine der gemessenen Abfragen.
    /// </remarks>
    internal static class LibrarySeed
    {
        /// <summary>Startwert des Zufallsgenerators; ändert er sich, ist die Messreihe gebrochen.</summary>
        public const int Seed = 20261120;

        // Die großen Serien des Bestands, wie sie im Abzug stehen; der Rest verteilt sich auf kleine.
        private static readonly int[] LargeSeriesEpisodes = [LibraryShape.LargestSeriesEpisodes, 240, 220, 200, 175, 160, 130];

        /// <summary>
        /// Füllt eine leere Datenbank mit dem Messbestand.
        /// </summary>
        /// <param name="context">Kontext auf einer frisch angelegten Datenbank.</param>
        /// <returns>Die Kennungen der abonnierten Serien, für Abfragen über den Bestand.</returns>
        public static IReadOnlyList<Guid> Fill(EchoPlayDbContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            SeededSequence random = new(Seed);

            List<Series> series = CreateSeries();
            context.Series.AddRange(series);

            List<Episode> episodes = CreateEpisodes(series, random);
            context.Episodes.AddRange(episodes);
            context.LocalTracks.AddRange(CreateTracks(episodes, random));
            context.PlaybackStates.AddRange(CreateStates(episodes, random));

            _ = context.SaveChanges();
            context.ChangeTracker.Clear();

            return [.. series.Where(s => s.IsSubscribed).Select(s => s.Id)];
        }

        private static List<Series> CreateSeries()
        {
            List<Series> series = new(LibraryShape.SeriesCount);
            for (int i = 0; i < LibraryShape.SeriesCount; i++)
            {
                bool local = i < LibraryShape.LocalSeriesCount;
                series.Add(new Series
                {
                    Title = $"Messserie {i:000}",
                    LocalFolderPath = local ? $@"D:\Hörspiele\Messserie {i:000}" : null,
                    IsOnlineImported = !local,
                    AppleMusicArtistId = local ? null : (100000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    IsSubscribed = i < LibraryShape.SubscribedSeriesCount,
                    IsWatched = i < LibraryShape.WatchedSeriesCount,
                    IsFavorite = i < LibraryShape.WatchedSeriesCount
                });
            }

            return series;
        }

        private static List<Episode> CreateEpisodes(List<Series> series, SeededSequence random)
        {
            int[] counts = EpisodeCounts(random);
            List<Episode> episodes = new(LibraryShape.EpisodeCount);
            DateTime firstRelease = new(1979, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            for (int s = 0; s < series.Count; s++)
            {
                for (int n = 1; n <= counts[s]; n++)
                {
                    // Etwa sechs von zehn Folgen einer lokalen Serie liegen auf der Platte, wie im
                    // Abzug (3.013 von 5.118, fast alle in lokalen Serien).
                    bool local = series[s].LocalFolderPath is not null && random.NextDouble() < 0.66;
                    episodes.Add(new Episode
                    {
                        Series = series[s],
                        Title = $"Folge {n:000}: Messfolge",
                        EpisodeNumber = n,
                        Duration = TimeSpan.FromMinutes(40 + random.Next(40)),
                        ReleaseDate = firstRelease.AddDays(random.Next(17_500)),
                        LocalFolderPath = local ? $@"{series[s].LocalFolderPath}\{n:000} - Messfolge" : null,
                        LocalTrackCount = local ? 1 + random.Next(4) : null,
                        TrackMatchKind = local ? TrackMatchKind.TbT : TrackMatchKind.NotMatched
                    });
                }
            }

            return episodes;
        }

        /// <summary>Folgen je Serie: die großen wie im Bestand, die übrigen zufällig, Summe genau <see cref="LibraryShape.EpisodeCount"/>.</summary>
        private static int[] EpisodeCounts(SeededSequence random)
        {
            int[] counts = new int[LibraryShape.SeriesCount];
            LargeSeriesEpisodes.CopyTo(counts, 0);

            int smallSeries = LibraryShape.SeriesCount - LargeSeriesEpisodes.Length;
            int remaining = LibraryShape.EpisodeCount - LargeSeriesEpisodes.Sum();
            for (int i = LargeSeriesEpisodes.Length; i < LibraryShape.SeriesCount; i++)
            {
                counts[i] = 1 + random.Next(2 * remaining / smallSeries);
            }

            // Auf die Summe ausgleichen, ohne eine Serie leer zu machen.
            int difference = LibraryShape.EpisodeCount - counts.Sum();
            for (int i = LargeSeriesEpisodes.Length; difference != 0; i = i + 1 < LibraryShape.SeriesCount ? i + 1 : LargeSeriesEpisodes.Length)
            {
                int step = Math.Sign(difference);
                if (counts[i] + step >= 1)
                {
                    counts[i] += step;
                    difference -= step;
                }
            }

            return counts;
        }

        private static IEnumerable<LocalTrack> CreateTracks(List<Episode> episodes, SeededSequence random)
        {
            foreach (Episode episode in episodes.Where(e => e.LocalTrackCount > 0))
            {
                for (int t = 1; t <= episode.LocalTrackCount; t++)
                {
                    yield return new LocalTrack
                    {
                        Episode = episode,
                        FilePath = $@"{episode.LocalFolderPath}\{t:00}.mp3",
                        TrackNumber = t,
                        Duration = TimeSpan.FromMinutes(10 + random.Next(20))
                    };
                }
            }
        }

        private static List<PlaybackState> CreateStates(List<Episode> episodes, SeededSequence random)
        {
            // Gehört wird vor allem, was auf der Platte liegt; deshalb kommen die lokalen Folgen zuerst
            // an die Reihe, gemischt innerhalb ihrer Gruppe.
            List<Episode> order = [.. episodes
                .OrderByDescending(e => e.LocalTrackCount > 0)
                .ThenBy(_ => random.Next(int.MaxValue))];

            DateTime now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
            List<PlaybackState> states = new(LibraryShape.CompletedStates + LibraryShape.InProgressStates);
            for (int i = 0; i < LibraryShape.CompletedStates + LibraryShape.InProgressStates; i++)
            {
                bool completed = i < LibraryShape.CompletedStates;
                DateTime played = now.AddMinutes(-random.Next(2 * 365 * 24 * 60));
                states.Add(new PlaybackState
                {
                    Episode = order[i],
                    IsCompleted = completed,
                    CompletedAt = completed ? played : null,
                    LastPlayedAt = played,
                    LastPosition = completed ? order[i].Duration : TimeSpan.FromMinutes(random.Next(30))
                });
            }

            return states;
        }
    }
}
