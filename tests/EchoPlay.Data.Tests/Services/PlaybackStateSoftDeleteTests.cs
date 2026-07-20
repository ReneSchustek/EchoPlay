using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Enthält Tests zur Verifikation des Soft-Delete-Verhaltens von PlaybackStates.
    /// Der Fokus liegt darauf sicherzustellen, dass das Löschen eines Wiedergabestatus keine Auswirkungen auf übergeordnete Entitäten hat.
    /// </summary>
    public class PlaybackStateSoftDeleteTests : DbTestBase
    {
        /// <summary>
        /// Stellt sicher, dass das Löschen eines PlaybackStates ausschließlich diesen selbst logisch löscht, während Episode und Serie unverändert
        /// bestehen bleiben.
        /// Dieser Test schützt explizit vor unbeabsichtigten Cascades und stellt sicher, dass der PlaybackState als rein technisches Kindelement
        /// behandelt wird.
        /// </summary>
        /// <returns>Ein asynchroner Task.</returns>
        [Fact]
        public async Task DeletingPlaybackState_SoftDeletesOnlyPlaybackState()
        {
            // Eine Serie ist notwendig, da alle weiteren Entitäten Teil derselben Foreign-Key-Hierarchie sind.
            Series series = await DataBuilder.PersistSeriesAsync("Serie");

            // Die Episode fungiert als fachlicher Container für den PlaybackState und darf durch dessen Löschung nicht beeinflusst werden.
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Episode");

            // Der PlaybackState wird explizit persistiert, da ausschließlich dessen Soft-Delete-Verhalten geprüft werden soll.
            PlaybackState playbackState = await DataBuilder.PersistPlaybackStateAsync(episode);

            PlaybackStateDataService service = new(Context, NullLoggerFactory);

            // Der Wiedergabestatus wird logisch gelöscht, ohne physisch aus der Datenbank entfernt zu werden.
            await service.DeleteAsync(playbackState.Id, cancellationToken: TestContext.Current.CancellationToken);

            // Der IgnoreQueryFilters-Aufruf ist erforderlich, um den tatsächlich persistierten Zustand unabhängig vom globalen Soft-Delete-Filter
            // überprüfen zu können.
            PlaybackState? persistedPlaybackState =
                await Context.PlaybackStates.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(entity => entity.Id == playbackState.Id, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(persistedPlaybackState);
            Assert.True(persistedPlaybackState.IsDeleted);

            // Die Episode muss unverändert bleiben, da kein fachlicher Zusammenhang zwischen PlaybackState-Löschung und Episode besteht.
            Episode? persistedEpisode =
                await Context.Episodes.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(entity => entity.Id == episode.Id, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(persistedEpisode);
            Assert.False(persistedEpisode.IsDeleted);

            // Auch die Serie darf durch das Löschen eines PlaybackStates nicht beeinflusst werden.
            Series? persistedSeries =
                await Context.Series.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(entity => entity.Id == series.Id, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotNull(persistedSeries);
            Assert.False(persistedSeries.IsDeleted);
        }
    }
}
