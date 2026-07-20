using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.DependencyInjection;
using EchoPlay.AppleMusic.Tests.Fakes;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.AppleMusic.Tests.Import
{
    /// <summary>
    /// Fachlicher Haupttest für den Apple-Music-Episodenimport.
    /// Der Test stellt sicher, dass aus der internen iTunes-Struktur
    /// eine flache und korrekt aggregierte Episodenliste entsteht.
    /// </summary>
    public sealed class AppleMusicEpisodeSourceScenarioTests
    {
        /// <summary>
        /// Stellt sicher, dass Episoden albumübergreifend korrekt aggregiert werden.
        /// Die Reihenfolge entspricht der Reihenfolge der Alben in der Lookup-Antwort.
        /// </summary>
        [Fact]
        public async Task GetEpisodesAsync_MultipleAlbums_ReturnsAggregatedEpisodes()
        {
            // ARRANGE
            // Der Szenario-Fake bildet eine realistische iTunes-Struktur ab.
            ServiceCollection services = new();

            // AddAppleMusicImport muss vor den Fakes aufgerufen werden, damit die Fake-Registrierungen die produktiven Services überschreiben.
            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions()));
            _ = services.AddAppleMusicImport();

            _ = services.AddSingleton<IAppleMusicSearchClient>(
                new ScenarioAppleMusicSearchClient());

            ServiceProvider provider = services.BuildServiceProvider();
            IEpisodeImportSource episodeImport = provider.GetRequiredService<IEpisodeImportSource>();

            // ACT
            IReadOnlyList<ImportEpisode> episodes = await episodeImport.GetEpisodesAsync("201306317", cancellationToken: TestContext.Current.CancellationToken);

            // ASSERT
            // Es müssen genau zwei Episoden erzeugt werden (eine pro Album).
            Assert.Equal(2, episodes.Count);

            // Die Episoden müssen in der Reihenfolge der Alben erscheinen.
            Assert.Contains("Späteres Album", episodes[0].Title, StringComparison.Ordinal);
            Assert.Contains("Früheres Album", episodes[1].Title, StringComparison.Ordinal);

            // Die OrderIndizes müssen fortlaufend sein.
            Assert.Equal(0, episodes[0].OrderIndex);
            Assert.Equal(1, episodes[1].OrderIndex);
        }

        /// <summary>
        /// Stellt sicher, dass die SourceEpisodeId korrekt aus der iTunes-Track-ID abgeleitet wird.
        /// </summary>
        [Fact]
        public async Task GetEpisodesAsync_TracksHaveCorrectSourceIds()
        {
            // ARRANGE
            ServiceCollection services = new();

            // AddAppleMusicImport muss vor den Fakes aufgerufen werden, damit die Fake-Registrierungen die produktiven Services überschreiben.
            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions()));
            _ = services.AddAppleMusicImport();

            _ = services.AddSingleton<IAppleMusicSearchClient>(
                new ScenarioAppleMusicSearchClient());

            ServiceProvider provider = services.BuildServiceProvider();
            IEpisodeImportSource episodeImport = provider.GetRequiredService<IEpisodeImportSource>();

            // ACT
            IReadOnlyList<ImportEpisode> episodes = await episodeImport.GetEpisodesAsync("201306317", cancellationToken: TestContext.Current.CancellationToken);

            // ASSERT
            // Die SourceEpisodeId muss die numerische iTunes-Track-ID als String enthalten.
            foreach (ImportEpisode episode in episodes)
            {
                Assert.False(string.IsNullOrWhiteSpace(episode.SourceEpisodeId));
                Assert.True(long.TryParse(episode.SourceEpisodeId, out long _));
            }
        }
    }
}
