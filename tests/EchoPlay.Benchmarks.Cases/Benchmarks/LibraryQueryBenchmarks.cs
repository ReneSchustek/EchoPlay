using BenchmarkDotNet.Attributes;
using EchoPlay.Benchmarks.Cases.Infrastructure;
using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Projections;
using EchoPlay.Data.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Benchmarks.Cases.Benchmarks
{
    /// <summary>
    /// Die Abfragen, die Startseite, Mediathek und Info-Leiste bei jedem Öffnen stellen, gegen eine
    /// SQLite-Datei mit dem Messbestand aus <see cref="LibrarySeed"/>.
    /// </summary>
    /// <remarks>
    /// Eine Datei statt einer Datenbank im Arbeitsspeicher, weil die App gegen eine Datei arbeitet:
    /// Seitencache und Dateizugriff gehören zur Messung. Jede Messung prüft ihre Trefferzahl; eine
    /// Abfrage, die nichts findet, wäre schnell und sagte nichts.
    /// </remarks>
    [MemoryDiagnoser]
    public class LibraryQueryBenchmarks
    {
        private readonly SilentLoggerFactory _loggers = new();
        private string _databasePath = string.Empty;
        private IReadOnlyList<Guid> _subscribedSeries = [];
        private EchoPlayDbContext? _context;

        /// <summary>Fängt die Befehle einer Messung für den Bericht der Abfragepläne mit.</summary>
        internal SqlCommandCapture? Capture { get; init; }

        /// <summary>Legt die Messdatenbank einmal je Messlauf an.</summary>
        [GlobalSetup]
        public void CreateDatabase()
        {
            _databasePath = Path.Combine(Path.GetTempPath(), $"echoplay_bench_{Guid.NewGuid():N}.db");
            using (EchoPlayDbContext seedContext = CreateContext())
            {
                _ = seedContext.Database.EnsureCreated();
                _subscribedSeries = LibrarySeed.Fill(seedContext);
            }

            _context = CreateContext();
        }

        /// <summary>Pfad der Messdatenbank, für den Bericht der Abfragepläne.</summary>
        internal string DatabasePath => _databasePath;

        /// <summary>Löscht die Messdatenbank.</summary>
        [GlobalCleanup]
        public void DeleteDatabase()
        {
            _context?.Dispose();
            SqliteConnection.ClearAllPools();
            File.Delete(_databasePath);
        }

        /// <summary>Abonnierte Serien, wie Startseite und Info-Leiste sie laden.</summary>
        /// <returns>Die Zahl der Serien.</returns>
        [Benchmark]
        public async Task<int> SubscribedSeries()
        {
            SeriesDataService service = new(Context, new WatchedTitleDataService(Context, _loggers), _loggers);
            IReadOnlyList<Series> series = await service.GetSubscribedAsync().ConfigureAwait(false);
            return Expect(series.Count, LibraryShape.SubscribedSeriesCount);
        }

        /// <summary>Alle Folgen der abonnierten Serien in einer Abfrage, wie die Info-Leiste zählt.</summary>
        /// <returns>Die Zahl der Folgen.</returns>
        [Benchmark]
        public async Task<int> EpisodesOfSubscribedSeries()
        {
            EpisodeDataService service = new(Context, _loggers);
            IReadOnlyList<Episode> episodes = await service.GetBySeriesIdsAsync(_subscribedSeries).ConfigureAwait(false);
            return ExpectAtLeast(episodes.Count, LibraryShape.EpisodeCount / 2);
        }

        /// <summary>Folgenzahl gesamt und lokal je Serie, wie die Kacheln der Mediathek sie zeigen.</summary>
        /// <returns>Die Zahl der Serien mit Zählung.</returns>
        [Benchmark]
        public async Task<int> EpisodeCountsPerSeries()
        {
            EpisodeDataService service = new(Context, _loggers);
            IReadOnlyDictionary<Guid, (int Total, int Local)> counts = await service.GetEpisodeCountsForSeriesAsync(_subscribedSeries).ConfigureAwait(false);
            return Expect(counts.Count, LibraryShape.SubscribedSeriesCount);
        }

        /// <summary>Alle Hörstände, wie die Info-Leiste die gehörten Folgen zählt.</summary>
        /// <returns>Die Zahl der Hörstände.</returns>
        [Benchmark]
        public async Task<int> AllPlaybackStates()
        {
            PlaybackStateDataService service = new(Context, _loggers);
            IReadOnlyList<PlaybackState> states = await service.GetAllAsync().ConfigureAwait(false);
            return Expect(states.Count, LibraryShape.CompletedStates + LibraryShape.InProgressStates);
        }

        /// <summary>Die zuletzt gehörten Folgen für „Weiterhören“ auf der Startseite.</summary>
        /// <returns>Die Zahl der Zeilen.</returns>
        [Benchmark]
        public async Task<int> RecentlyPlayed()
        {
            PlaybackStateDataService service = new(Context, _loggers);
            IReadOnlyList<RecentPlaybackRow> rows = await service.GetRecentActiveAsync(20).ConfigureAwait(false);
            return ExpectAtLeast(rows.Count, 1);
        }

        private EchoPlayDbContext Context => _context ?? throw new InvalidOperationException("GlobalSetup ist nicht gelaufen.");

        private EchoPlayDbContext CreateContext()
        {
            DbContextOptionsBuilder<EchoPlayDbContext> builder = new DbContextOptionsBuilder<EchoPlayDbContext>()
                .UseSqlite($"Data Source={_databasePath}")
                .AddInterceptors(new SqlitePragmaInterceptor())
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            if (Capture is not null)
            {
                _ = builder.AddInterceptors(Capture);
            }

            return new EchoPlayDbContext(builder.Options);
        }

        private static int Expect(int actual, int expected) => actual == expected
            ? actual
            : throw new InvalidOperationException($"Erwartet {expected} Zeilen, gefunden {actual}: Die Messung liefe über den falschen Bestand.");

        private static int ExpectAtLeast(int actual, int minimum) => actual >= minimum
            ? actual
            : throw new InvalidOperationException($"Erwartet mindestens {minimum} Zeilen, gefunden {actual}: Die Messung liefe über den falschen Bestand.");
    }
}
