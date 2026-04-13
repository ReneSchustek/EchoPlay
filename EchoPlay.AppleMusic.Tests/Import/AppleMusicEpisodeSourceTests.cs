using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.DependencyInjection;
using EchoPlay.AppleMusic.Tests.Fakes;
using EchoPlay.AppleMusic.Tests.TestData;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.AppleMusic.Tests.Import
{
    /// <summary>
    /// Fachliche Tests für den Apple-Music-Episodenimport.
    /// Diese Tests prüfen ausschließlich das vertraglich zugesicherte Verhalten des IEpisodeImportSource.
    /// </summary>
    public sealed class AppleMusicEpisodeSourceTests
    {
        /// <summary>
        /// Stellt sicher, dass bei einer Serie ohne verfügbare Alben
        /// keine Episoden erzeugt werden.
        /// </summary>
        [Fact]
        public async Task GetEpisodesAsync_SeriesWithoutAlbums_ReturnsEmptyList()
        {
            // ARRANGE
            // Der Fake liefert bewusst nur Künstlerdaten.
            // Alben und Tracks sind nicht vorhanden.
            ServiceCollection services = new();

            // AddAppleMusicImport muss vor den Fakes aufgerufen werden, damit die Fake-Registrierungen die produktiven Services überschreiben.
            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions()));
            _ = services.AddAppleMusicImport();

            _ = services.AddSingleton<IAppleMusicSearchClient>(
                new FakeAppleMusicSearchClient(
                    artists: [AppleMusicTestData.DieDreiFragezeichen]));

            ServiceProvider provider = services.BuildServiceProvider();
            IEpisodeImportSource episodeImport = provider.GetRequiredService<IEpisodeImportSource>();

            // ACT
            IReadOnlyList<ImportEpisode> episodes = await episodeImport.GetEpisodesAsync("201306317");

            // ASSERT
            // Ohne Alben dürfen keine Episoden entstehen.
            Assert.Empty(episodes);
        }

        /// <summary>
        /// Stellt sicher, dass eine ungültige SourceSeriesId zu einer ArgumentException führt.
        /// </summary>
        [Fact]
        public async Task GetEpisodesAsync_InvalidSourceSeriesId_ThrowsArgumentException()
        {
            // ARRANGE
            ServiceCollection services = new();

            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions()));
            _ = services.AddAppleMusicImport();

            _ = services.AddSingleton<IAppleMusicSearchClient>(
                new FakeAppleMusicSearchClient(artists: []));

            ServiceProvider provider = services.BuildServiceProvider();
            IEpisodeImportSource episodeImport = provider.GetRequiredService<IEpisodeImportSource>();

            // ACT & ASSERT
            _ = await Assert.ThrowsAsync<ArgumentException>(
                () => episodeImport.GetEpisodesAsync("keine-gueltige-id"));
        }
    }
}
