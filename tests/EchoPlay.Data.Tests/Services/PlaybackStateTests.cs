using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Enthält grundlegende Tests für den PlaybackStateDataService.
    /// Der Fokus liegt auf dem korrekten Persistieren und Wiederauffinden von Wiedergabeständen über ihre zugehörige Episode.
    /// </summary>
    public class PlaybackStateTests : DbTestBase
    {
        /// <summary>
        /// Stellt sicher, dass ein gespeicherter PlaybackState über die EpisodeId korrekt wieder abgerufen werden kann.
        /// Dieser Test validiert das grundlegende Zusammenspiel zwischen Datenbankpersistenz, Foreign-Key-Beziehung und Abfrageverhalten
        /// des Services ohne Berücksichtigung von Soft-Delete oder Cascades.
        /// </summary>
        /// <returns>Ein asynchroner Task.</returns>
        [Fact]
        public async Task AddAndGetByEpisodeId_ReturnsPlaybackState()
        {
            // Eine Serie wird benötigt, da Episoden ohne gültige Elternserie aufgrund von Foreign-Key-Constraints nicht persistiert werden können.
            Series series = await DataBuilder.PersistSeriesAsync("Serie");

            // Die Episode stellt die fachliche Klammer für den PlaybackState dar und muss vor dem Wiedergabestatus physisch existieren.
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Episode");

            // Der PlaybackState selbst enthält keine Fachlogik zur Erstellung, sondern wird bewusst direkt im Test konfiguriert, um das
            // Serviceverhalten isoliert zu prüfen.
            PlaybackState playbackState = new()
            {
                EpisodeId = episode.Id,
                LastPosition = TimeSpan.Zero
            };

            PlaybackStateDataService service = new(Context, NullLoggerFactory);

            // Der Wiedergabestatus wird explizit über den Service persistiert, um sicherzustellen, dass der produktive Codepfad getestet wird.
            await service.AddAsync(playbackState, cancellationToken: TestContext.Current.CancellationToken);

            // Der Abruf erfolgt über die EpisodeId, da dies der fachlich relevante Zugriffspfad innerhalb der Anwendung ist.
            PlaybackState? result = await service.GetByEpisodeIdAsync(episode.Id, cancellationToken: TestContext.Current.CancellationToken);

            // Es muss exakt der zuvor gespeicherte PlaybackState zurückgegeben werden.
            Assert.NotNull(result);
            Assert.Equal(playbackState.Id, result.Id);
        }

        /// <summary>
        /// Ohne vorhandenen Wiedergabestatus legt <c>MarkCompletedAsync</c> einen neuen an,
        /// der als abgeschlossen markiert ist und den übergebenen Zeitpunkt trägt.
        /// </summary>
        [Fact]
        public async Task MarkCompletedAsync_NoExistingState_CreatesCompletedState()
        {
            Series series = await DataBuilder.PersistSeriesAsync("Serie");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Episode");

            DateTime completedAt = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);
            PlaybackStateDataService service = new(Context, NullLoggerFactory);

            await service.MarkCompletedAsync(episode.Id, completedAt, cancellationToken: TestContext.Current.CancellationToken);

            PlaybackState? result = await service.GetByEpisodeIdAsync(episode.Id, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(result);
            Assert.True(result.IsCompleted);
            Assert.Equal(completedAt, result.CompletedAt);
            Assert.Equal(completedAt, result.LastPlayedAt);
        }

        /// <summary>
        /// Mit vorhandenem Wiedergabestatus aktualisiert <c>MarkCompletedAsync</c> diesen auf abgeschlossen,
        /// ohne einen zweiten Eintrag anzulegen.
        /// </summary>
        [Fact]
        public async Task MarkCompletedAsync_ExistingState_UpdatesToCompleted()
        {
            Series series = await DataBuilder.PersistSeriesAsync("Serie");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Episode");

            PlaybackStateDataService service = new(Context, NullLoggerFactory);
            await service.AddAsync(
                new PlaybackState { EpisodeId = episode.Id, LastPosition = TimeSpan.FromMinutes(5) },
                cancellationToken: TestContext.Current.CancellationToken);

            // Produktiv läuft jede Operation in einem frischen Scope; im Test den Tracker leeren.
            Context.ChangeTracker.Clear();

            DateTime completedAt = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);
            await service.MarkCompletedAsync(episode.Id, completedAt, cancellationToken: TestContext.Current.CancellationToken);

            IReadOnlyList<PlaybackState> all = await service.GetAllAsync(cancellationToken: TestContext.Current.CancellationToken);
            PlaybackState state = Assert.Single(all);
            Assert.True(state.IsCompleted);
            Assert.Equal(completedAt, state.CompletedAt);
        }

        /// <summary>
        /// <c>MarkNotStartedAsync</c> entfernt einen vorhandenen Wiedergabestatus.
        /// </summary>
        [Fact]
        public async Task MarkNotStartedAsync_ExistingState_RemovesState()
        {
            Series series = await DataBuilder.PersistSeriesAsync("Serie");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Episode");

            PlaybackStateDataService service = new(Context, NullLoggerFactory);
            await service.AddAsync(
                new PlaybackState { EpisodeId = episode.Id, IsCompleted = true },
                cancellationToken: TestContext.Current.CancellationToken);

            // Produktiv läuft jede Operation in einem frischen Scope; im Test den Tracker leeren.
            Context.ChangeTracker.Clear();

            await service.MarkNotStartedAsync(episode.Id, cancellationToken: TestContext.Current.CancellationToken);

            PlaybackState? result = await service.GetByEpisodeIdAsync(episode.Id, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Null(result);
        }

        /// <summary>
        /// Ohne vorhandenen Wiedergabestatus ist <c>MarkNotStartedAsync</c> ein No-Op ohne Fehler.
        /// </summary>
        [Fact]
        public async Task MarkNotStartedAsync_NoState_DoesNothing()
        {
            Series series = await DataBuilder.PersistSeriesAsync("Serie");
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Episode");

            PlaybackStateDataService service = new(Context, NullLoggerFactory);

            await service.MarkNotStartedAsync(episode.Id, cancellationToken: TestContext.Current.CancellationToken);

            PlaybackState? result = await service.GetByEpisodeIdAsync(episode.Id, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Null(result);
        }
    }
}
