using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Enthält Tests zur Verifikation des Kaskadenverhaltens beim Löschen einer Episode. Der Fokus liegt darauf sicherzustellen, dass das
    /// Löschen einer Episode korrekt auf untergeordnete PlaybackStates wirkt, ohne die übergeordnete Serie zu beeinflussen.
    /// </summary>
    public class EpisodeCascadeDeleteTests : DbTestBase
    {
        /// <summary>
        /// Stellt sicher, dass das Löschen einer Episode alle zugehörigen PlaybackStates logisch löscht, während die zugehörige Serie
        /// unverändert bestehen bleibt.
        /// Dieser Test bildet eine zentrale fachliche Regel ab: Episoden dürfen nicht ohne Wiedergabestatus verschwinden, gleichzeitig
        /// darf eine Serie nicht implizit gelöscht werden.
        /// </summary>
        /// <returns>Ein asynchroner Task.</returns>
        [Fact]
        public async Task DeletingEpisode_SoftDeletesPlaybackState_ButNotSeries()
        {
            // Die Serie bildet den fachlichen Aggregatwurzelpunkt und darf durch das Löschen einer Episode nicht beeinflusst werden.
            Series series = await DataBuilder.PersistSeriesAsync("Serie");

            // Die Episode ist der fachliche Besitzer der PlaybackStates un stellt den Ausgangspunkt für das Kaskadenverhalten dar.
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Episode");

            // Der PlaybackState ist technisch an die Episode gebunden und muss beim Löschen der Episode ebenfalls logisch entfernt werden.
            PlaybackState playbackState = await DataBuilder.PersistPlaybackStateAsync(episode);

            EpisodeDataService service = new(Context, NullLoggerFactory);

            // Das Löschen der Episode löst bewusst die Kaskade auf die untergeordneten PlaybackStates aus.
            await service.DeleteAsync(episode.Id);

            // Der IgnoreQueryFilters-Aufruf ist notwendig, um den tatsächlichen Persistenzzustand unabhängig vom globalen Soft-Delete-Filter
            // überprüfen zu können.
            Episode? persistedEpisode =
                await Context.Episodes.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(entity => entity.Id == episode.Id);

            Assert.NotNull(persistedEpisode);
            Assert.True(persistedEpisode.IsDeleted);

            // PlaybackStates müssen ebenfalls logisch gelöscht sein, da sie ohne zugehörige Episode keinen gültigen fachlichen Zustand
            // darstellen.
            PlaybackState? persistedPlaybackState =
                await Context.PlaybackStates.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(entity => entity.Id == playbackState.Id);

            Assert.NotNull(persistedPlaybackState);
            Assert.True(persistedPlaybackState.IsDeleted);

            // Die Serie darf nicht beeinflusst werden, da das Löschen einer Episode keine fachliche Bedeutung für die Existenz der Serie hat.
            Series? persistedSeries =
                await Context.Series.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(entity => entity.Id == series.Id);

            Assert.NotNull(persistedSeries);
            Assert.False(persistedSeries.IsDeleted);
        }
    }
}