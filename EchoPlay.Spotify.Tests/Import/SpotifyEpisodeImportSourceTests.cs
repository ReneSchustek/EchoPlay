using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.DependencyInjection;
using EchoPlay.Spotify.Tests.Fakes;
using EchoPlay.Spotify.Tests.TestData;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EchoPlay.Spotify.Tests.Import
{
    /// <summary>
    /// Fachliche Tests für den Spotify-Episodenimport.
    /// Diese Tests prüfen ausschließlich das vertraglich zugesicherte Verhalten des IEpisodeImportSource.
    /// </summary>
    public sealed class SpotifyEpisodeImportSourceTests
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

            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions()));

            _ = services.AddSingleton<ISpotifyApiClient>(
                new FakeSpotifyApiClient(
                    artists: [SpotifyTestData.DieDreiFragezeichen]));

            _ = services.AddSpotifyImport();

            ServiceProvider provider = services.BuildServiceProvider();
            IEpisodeImportSource episodeImport = provider.GetRequiredService<IEpisodeImportSource>();

            // ACT
            IReadOnlyList<ImportEpisode> episodes = await episodeImport.GetEpisodesAsync("artist-ddf");

            // ASSERT
            // Ohne Alben dürfen keine Episoden entstehen.
            Assert.Empty(episodes);
        }
    }
}
