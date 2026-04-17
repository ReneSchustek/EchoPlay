using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Enthält Tests zur Verifikation des Kaskadenverhaltens beim Löschen einer Episode in Bezug auf zugehörige PlaybackStates.
    /// Der Fokus liegt darauf sicherzustellen, dass PlaybackStates korrekt logisch gelöscht werden, ohne die übergeordnete Serie zu beeinflussen.
    /// </summary>
    public class PlaybackStateCascadeDeleteTests : DbTestBase
    {
        /// <summary>
        /// Stellt sicher, dass das Löschen einer Episode alle zugehörigen PlaybackStates logisch löscht, während die zugehörige Serie
        /// unverändert bestehen bleibt.
        /// Dieser Test schützt vor unvollständigen Cascades und stellt sicher, dass Wiedergabestände nicht verwaist oder sichtbar bleiben, nachdem
        /// ihre Episode logisch entfernt wurde.
        /// </summary>
        /// <returns>Ein asynchroner Task.</returns>
        [Fact]
        public async Task DeletingEpisode_SoftDeletesPlaybackState()
        {
            // Die Serie bildet den fachlichen Wurzelknoten und darf durch das Löschen einer Episode nicht beeinflusst werden.
            Series series = await DataBuilder.PersistSeriesAsync("Serie");

            // Die Episode fungiert als fachlicher Besitzer der PlaybackStates und ist der Auslöser für das Kaskadenverhalten.
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Episode");

            // Der PlaybackState ist technisch an die Episode gebunden und muss beim Löschen der Episode automatisch logisch entfernt werden.
            PlaybackState playbackState = await DataBuilder.PersistPlaybackStateAsync(episode);

            EpisodeDataService service = new(Context, NullLoggerFactory);

            // Die Episode wird logisch gelöscht, wodurch die Cascade auf zugehörige PlaybackStates ausgelöst wird.
            await service.DeleteAsync(episode.Id);

            // Der IgnoreQueryFilters-Aufruf ist erforderlich, um den tatsächlichen Persistenzzustand unabhängig vom globalen Soft-Delete-Filter zu prüfen.
            PlaybackState? persistedPlaybackState =
                await Context.PlaybackStates.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(entity => entity.Id == playbackState.Id);

            Assert.NotNull(persistedPlaybackState);
            Assert.True(persistedPlaybackState.IsDeleted);

            // Die Episode selbst muss ebenfalls logisch gelöscht sein, da sie explizit über den Service entfernt wurde.
            Episode? persistedEpisode =
                await Context.Episodes.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(entity => entity.Id == episode.Id);

            Assert.NotNull(persistedEpisode);
            Assert.True(persistedEpisode.IsDeleted);

            // Die Serie darf durch das Löschen einer Episode nicht beeinflusst werden, da keine fachliche Abhängigkeit in diese Richtung besteht.
            Series? persistedSeries =
                await Context.Series.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(entity => entity.Id == series.Id);

            Assert.NotNull(persistedSeries);
            Assert.False(persistedSeries.IsDeleted);
        }
    }
}
