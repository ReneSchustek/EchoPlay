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
            await service.AddAsync(playbackState);

            // Der Abruf erfolgt über die EpisodeId, da dies der fachlich relevante Zugriffspfad innerhalb der Anwendung ist.
            PlaybackState? result = await service.GetByEpisodeIdAsync(episode.Id);

            // Es muss exakt der zuvor gespeicherte PlaybackState zurückgegeben werden.
            Assert.NotNull(result);
            Assert.Equal(playbackState.Id, result.Id);
        }
    }
}