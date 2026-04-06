using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services;
using EchoPlay.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Services
{
    /// <summary>
    /// Enthält Tests zur Verifikation des vollständigen Soft-Delete-Cascade-Verhaltens
    /// beim Löschen einer Serie. Diese Tests sichern die oberste Ebene der Delete-Matrix ab.
    /// </summary>
    public class SeriesCascadeDeleteTests : DbTestBase
    {
        /// <summary>
        /// Stellt sicher, dass das Löschen einer Serie alle untergeordneten Episoden sowie deren PlaybackStates logisch löscht.
        /// Dieser Test bildet die wichtigste fachliche Invariante des Datenmodells ab:
        /// Eine gelöschte Serie darf keine sichtbaren oder aktiven Kindelemente behalten.
        /// </summary>
        /// <returns>Ein asynchroner Task.</returns>
        [Fact]
        public async Task DeletingSeries_SoftDeletesEpisodesAndPlaybackStates()
        {
            // Die Serie bildet den Aggregatwurzelpunkt und ist der alleinige Auslöser für das vollständige Kaskadenverhalten.
            Series series = await DataBuilder.PersistSeriesAsync("Serie");

            // Die Episode ist fachlich vollständig von der Serie abhängig und darf nach deren Löschung nicht mehr sichtbar sein.
            Episode episode = await DataBuilder.PersistEpisodeAsync(series, "Episode");

            // Der PlaybackState ist ein rein technisches Kindelement der Episode und muss ebenfalls logisch entfernt werden.
            PlaybackState playbackState = await DataBuilder.PersistPlaybackStateAsync(episode);

            SeriesDataService service = new(Context, NullLoggerFactory);

            // Das Löschen der Serie löst bewusst die vollständige Cascade über Episoden bis hin zu PlaybackStates aus.
            await service.DeleteAsync(series.Id);

            // IgnoreQueryFilters ist erforderlich, um den tatsächlichen Persistenzzustand unabhängig vom Soft-Delete-Filter zu prüfen.
            Series? persistedSeries =
                await Context.Series.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(entity => entity.Id == series.Id);

            Assert.NotNull(persistedSeries);
            Assert.True(persistedSeries.IsDeleted);

            // Alle Episoden der Serie müssen logisch gelöscht sein, da sie ohne ihre Serie keinen gültigen fachlichen Zustand besitzen.
            Episode? persistedEpisode =
                await Context.Episodes.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(entity => entity.Id == episode.Id);

            Assert.NotNull(persistedEpisode);
            Assert.True(persistedEpisode.IsDeleted);

            // PlaybackStates dürfen nach dem Löschen der Serie nicht mehr aktiv oder sichtbar bleiben.
            PlaybackState? persistedPlaybackState =
                await Context.PlaybackStates.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(entity => entity.Id == playbackState.Id);

            Assert.NotNull(persistedPlaybackState);
            Assert.True(persistedPlaybackState.IsDeleted);
        }
    }
}