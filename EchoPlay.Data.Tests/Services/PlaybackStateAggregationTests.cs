using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Internal;
using EchoPlay.Data.Services;
using EchoPlay.Data.Services.Projections;
using EchoPlay.Data.Tests.Helper;
using EchoPlay.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Verifiziert die serverseitigen Aggregationen des <see cref="PlaybackStateDataService"/>.
    /// Geprüft werden Korrektheit der Zähler, Soft-Delete-Filter, Sortierung
    /// sowie die Single-Query-Eigenschaft (Akzeptanz aus Brief 275).
    /// </summary>
    public sealed class PlaybackStateAggregationTests : IDisposable
    {
        private static readonly EchoPlay.Logger.Abstractions.ILoggerFactory NullLoggerFactory =
            new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions());

        private readonly CountingCommandInterceptor _interceptor;
        private readonly EchoPlayDbContext _context;
        private readonly TestDataBuilder _builder;

        /// <summary>
        /// Initialisiert eine isolierte SQLite-In-Memory-DB mit angehängtem Command-Counter.
        /// </summary>
        public PlaybackStateAggregationTests()
        {
            _interceptor = new CountingCommandInterceptor();
            _context = SqliteInMemoryDbContextFactory.Create(_interceptor);
            _builder = new TestDataBuilder(_context);
        }

        /// <summary>
        /// Gibt den DbContext frei.
        /// </summary>
        public void Dispose()
        {
            _context.Dispose();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Stellt sicher, dass die Aggregation alle drei Kategorien korrekt zählt
        /// (1× Finished, 1× InProgress, 1× NotStarted).
        /// </summary>
        [Fact]
        public async Task GetCounts_WithCompleted_InProgress_NotStarted_ReturnsCorrectShape()
        {
            Series series = await _builder.PersistSeriesAsync("Serie");
            Episode finishedEpisode = await _builder.PersistEpisodeAsync(series, "Finished");
            Episode inProgressEpisode = await _builder.PersistEpisodeAsync(series, "InProgress");
            _ = await _builder.PersistEpisodeAsync(series, "NotStarted");

            // Finished: IsCompleted = true.
            PlaybackState finishedState = new()
            {
                EpisodeId = finishedEpisode.Id,
                IsCompleted = true,
                LastPosition = TimeSpan.FromMinutes(30)
            };
            _ = _context.PlaybackStates.Add(finishedState);

            // InProgress: !IsCompleted, LastPosition > 0.
            PlaybackState inProgressState = new()
            {
                EpisodeId = inProgressEpisode.Id,
                IsCompleted = false,
                LastPosition = TimeSpan.FromMinutes(5)
            };
            _ = _context.PlaybackStates.Add(inProgressState);

            _ = await _context.SaveChangesAsync();

            PlaybackStateDataService service = new(_context, NullLoggerFactory);

            (int finished, int inProgress, int notStarted) = await service.GetCountsBySeriesIdAsync(series.Id);

            Assert.Equal(1, finished);
            Assert.Equal(1, inProgress);
            Assert.Equal(1, notStarted);
        }

        /// <summary>
        /// Eine Serie ohne Episoden liefert (0, 0, 0).
        /// </summary>
        [Fact]
        public async Task GetCounts_EmptySeries_ReturnsZeros()
        {
            Series series = await _builder.PersistSeriesAsync("Leere Serie");

            PlaybackStateDataService service = new(_context, NullLoggerFactory);

            (int finished, int inProgress, int notStarted) = await service.GetCountsBySeriesIdAsync(series.Id);

            Assert.Equal(0, finished);
            Assert.Equal(0, inProgress);
            Assert.Equal(0, notStarted);
        }

        /// <summary>
        /// Soft-deletete PlaybackStates dürfen nicht gezählt werden – die Episode rutscht
        /// damit zurück in die NotStarted-Kategorie.
        /// </summary>
        [Fact]
        public async Task GetCounts_OnlyDeletedStates_ReturnsZerosForFinishedAndInProgress()
        {
            Series series = await _builder.PersistSeriesAsync("Serie");
            Episode episode = await _builder.PersistEpisodeAsync(series, "Episode");

            PlaybackState state = new()
            {
                EpisodeId = episode.Id,
                IsCompleted = true,
                LastPosition = TimeSpan.FromMinutes(15)
            };
            _ = _context.PlaybackStates.Add(state);
            _ = await _context.SaveChangesAsync();

            // Logisch löschen über die Domänen-API.
            state.MarkAsDeleted(EntityClock.Current.UtcNow);
            _ = _context.PlaybackStates.Update(state);
            _ = await _context.SaveChangesAsync();

            PlaybackStateDataService service = new(_context, NullLoggerFactory);

            (int finished, int inProgress, int notStarted) = await service.GetCountsBySeriesIdAsync(series.Id);

            Assert.Equal(0, finished);
            Assert.Equal(0, inProgress);
            Assert.Equal(1, notStarted);
        }

        /// <summary>
        /// Akzeptanzkriterium: <see cref="PlaybackStateDataService.GetCountsBySeriesIdAsync"/>
        /// erzeugt genau ein SELECT (kein N+1, keine Entity-Materialisierung).
        /// </summary>
        [Fact]
        public async Task GetCounts_PerformsSingleSelectQuery()
        {
            Series series = await _builder.PersistSeriesAsync("Serie");
            Episode episode = await _builder.PersistEpisodeAsync(series, "Episode");
            _ = await _builder.PersistPlaybackStateAsync(episode);

            PlaybackStateDataService service = new(_context, NullLoggerFactory);

            _interceptor.Reset();
            _ = await service.GetCountsBySeriesIdAsync(series.Id);

            Assert.Equal(1, _interceptor.SelectCount);
        }

        /// <summary>
        /// Stellt sicher, dass die jüngste Aktivität an erster Stelle steht.
        /// </summary>
        [Fact]
        public async Task GetRecentActive_OrdersByLastPlayedAtDescending()
        {
            Series series = await _builder.PersistSeriesAsync("Serie");
            Episode olderEpisode = await _builder.PersistEpisodeAsync(series, "Älter");
            Episode newerEpisode = await _builder.PersistEpisodeAsync(series, "Neuer");

            DateTime now = DateTime.UtcNow;

            PlaybackState olderState = new()
            {
                EpisodeId = olderEpisode.Id,
                LastPosition = TimeSpan.FromMinutes(5),
                LastPlayedAt = now.AddHours(-2)
            };
            PlaybackState newerState = new()
            {
                EpisodeId = newerEpisode.Id,
                LastPosition = TimeSpan.FromMinutes(10),
                LastPlayedAt = now.AddMinutes(-5)
            };
            _context.PlaybackStates.AddRange(olderState, newerState);
            _ = await _context.SaveChangesAsync();

            PlaybackStateDataService service = new(_context, NullLoggerFactory);

            IReadOnlyList<RecentPlaybackRow> rows = await service.GetRecentActiveAsync(maxRows: 10);

            Assert.Equal(2, rows.Count);
            Assert.Equal(newerState.EpisodeId, rows[0].EpisodeId);
            Assert.Equal(olderState.EpisodeId, rows[1].EpisodeId);
        }

        /// <summary>
        /// Verifiziert, dass <c>maxRows</c> serverseitig limitiert (TOP-N).
        /// </summary>
        [Fact]
        public async Task GetRecentActive_HonoursMaxRows()
        {
            Series series = await _builder.PersistSeriesAsync("Serie");
            DateTime now = DateTime.UtcNow;

            for (int i = 0; i < 5; i++)
            {
                Episode episode = await _builder.PersistEpisodeAsync(series, $"Folge {i}");
                PlaybackState state = new()
                {
                    EpisodeId = episode.Id,
                    LastPosition = TimeSpan.FromMinutes(1 + i),
                    LastPlayedAt = now.AddMinutes(-i)
                };
                _ = _context.PlaybackStates.Add(state);
            }

            _ = await _context.SaveChangesAsync();

            PlaybackStateDataService service = new(_context, NullLoggerFactory);

            IReadOnlyList<RecentPlaybackRow> rows = await service.GetRecentActiveAsync(maxRows: 2);

            Assert.Equal(2, rows.Count);
        }

        /// <summary>
        /// Filtert serverseitig: nur IsCompleted oder LastPosition &gt; 0 darf erscheinen.
        /// Ein „leerer" PlaybackState (Position 0, nicht abgeschlossen) wird ausgeschlossen.
        /// </summary>
        [Fact]
        public async Task GetRecentActive_IncludesCompletedAndInProgressOnly()
        {
            Series series = await _builder.PersistSeriesAsync("Serie");
            Episode emptyEpisode = await _builder.PersistEpisodeAsync(series, "Empty");
            Episode completedEpisode = await _builder.PersistEpisodeAsync(series, "Completed");
            Episode inProgressEpisode = await _builder.PersistEpisodeAsync(series, "InProgress");

            DateTime now = DateTime.UtcNow;

            // Darf NICHT erscheinen.
            PlaybackState emptyState = new()
            {
                EpisodeId = emptyEpisode.Id,
                LastPosition = TimeSpan.Zero,
                IsCompleted = false,
                LastPlayedAt = now
            };
            PlaybackState completedState = new()
            {
                EpisodeId = completedEpisode.Id,
                IsCompleted = true,
                LastPosition = TimeSpan.FromMinutes(30),
                LastPlayedAt = now.AddMinutes(-1)
            };
            PlaybackState inProgressState = new()
            {
                EpisodeId = inProgressEpisode.Id,
                LastPosition = TimeSpan.FromMinutes(5),
                LastPlayedAt = now.AddMinutes(-2)
            };
            _context.PlaybackStates.AddRange(emptyState, completedState, inProgressState);
            _ = await _context.SaveChangesAsync();

            PlaybackStateDataService service = new(_context, NullLoggerFactory);

            IReadOnlyList<RecentPlaybackRow> rows = await service.GetRecentActiveAsync(maxRows: 10);

            Assert.Equal(2, rows.Count);
            Assert.DoesNotContain(rows, r => r.EpisodeId == emptyEpisode.Id);
            Assert.Contains(rows, r => r.EpisodeId == completedEpisode.Id);
            Assert.Contains(rows, r => r.EpisodeId == inProgressEpisode.Id);
        }

        /// <summary>
        /// Soft-deletete States dürfen nicht in der Liste auftauchen – global Query Filter greift.
        /// </summary>
        [Fact]
        public async Task GetRecentActive_ExcludesSoftDeletedStates()
        {
            Series series = await _builder.PersistSeriesAsync("Serie");
            Episode episode = await _builder.PersistEpisodeAsync(series, "Episode");

            PlaybackState state = new()
            {
                EpisodeId = episode.Id,
                LastPosition = TimeSpan.FromMinutes(5),
                LastPlayedAt = DateTime.UtcNow
            };
            _ = _context.PlaybackStates.Add(state);
            _ = await _context.SaveChangesAsync();

            state.MarkAsDeleted(EntityClock.Current.UtcNow);
            _ = _context.PlaybackStates.Update(state);
            _ = await _context.SaveChangesAsync();

            PlaybackStateDataService service = new(_context, NullLoggerFactory);

            IReadOnlyList<RecentPlaybackRow> rows = await service.GetRecentActiveAsync(maxRows: 10);

            Assert.Empty(rows);
        }

        /// <summary>
        /// Negative oder Null-Werte für <c>maxRows</c> erzeugen weder Query noch Allokation –
        /// Schutz vor versehentlich entgrenzten DB-Aufrufen.
        /// </summary>
        [Fact]
        public async Task GetRecentActive_NonPositiveMaxRows_ReturnsEmptyWithoutQuery()
        {
            PlaybackStateDataService service = new(_context, NullLoggerFactory);

            _interceptor.Reset();
            IReadOnlyList<RecentPlaybackRow> rows = await service.GetRecentActiveAsync(maxRows: 0);

            Assert.Empty(rows);
            Assert.Equal(0, _interceptor.SelectCount);
        }

        /// <summary>
        /// Verifiziert die Soft-Delete-Filter-Wirkung in der compiled-Query-Variante:
        /// nachträglich gelöschte States verschwinden bei einem erneuten Aufruf, das
        /// einmal erzeugte Delegate liefert kein veraltetes Ergebnis.
        /// </summary>
        [Fact]
        public async Task GetRecentActive_SoftDeleteAfterFirstCall_DoesNotLeakInSecondCall()
        {
            Series series = await _builder.PersistSeriesAsync("Serie");
            Episode episode = await _builder.PersistEpisodeAsync(series, "Episode");

            PlaybackState state = new()
            {
                EpisodeId = episode.Id,
                LastPosition = TimeSpan.FromMinutes(5),
                LastPlayedAt = DateTime.UtcNow
            };
            _ = _context.PlaybackStates.Add(state);
            _ = await _context.SaveChangesAsync();

            PlaybackStateDataService service = new(_context, NullLoggerFactory);

            IReadOnlyList<RecentPlaybackRow> beforeDelete = await service.GetRecentActiveAsync(maxRows: 10);
            _ = Assert.Single(beforeDelete);

            state.MarkAsDeleted(EntityClock.Current.UtcNow);
            _ = _context.PlaybackStates.Update(state);
            _ = await _context.SaveChangesAsync();

            IReadOnlyList<RecentPlaybackRow> afterDelete = await service.GetRecentActiveAsync(maxRows: 10);
            Assert.Empty(afterDelete);
        }

        /// <summary>
        /// Sichert ab, dass die compiled Query bei wiederholten Aufrufen mit unterschiedlichen
        /// <c>maxRows</c>-Werten unabhängig korrekt limitiert – das Delegate cached keine Parameter.
        /// </summary>
        [Fact]
        public async Task GetRecentActive_RepeatedCallsWithDifferentLimits_ReturnIndependentResults()
        {
            Series series = await _builder.PersistSeriesAsync("Serie");
            DateTime now = DateTime.UtcNow;

            for (int i = 0; i < 4; i++)
            {
                Episode episode = await _builder.PersistEpisodeAsync(series, $"Folge {i}");
                PlaybackState state = new()
                {
                    EpisodeId = episode.Id,
                    LastPosition = TimeSpan.FromMinutes(1 + i),
                    LastPlayedAt = now.AddMinutes(-i)
                };
                _ = _context.PlaybackStates.Add(state);
            }

            _ = await _context.SaveChangesAsync();

            PlaybackStateDataService service = new(_context, NullLoggerFactory);

            IReadOnlyList<RecentPlaybackRow> two = await service.GetRecentActiveAsync(maxRows: 2);
            IReadOnlyList<RecentPlaybackRow> four = await service.GetRecentActiveAsync(maxRows: 4);

            Assert.Equal(2, two.Count);
            Assert.Equal(4, four.Count);
        }
    }
}
