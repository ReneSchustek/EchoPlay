using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.DependencyInjection;
using EchoPlay.Spotify.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EchoPlay.Spotify.Tests.Import
{
    /// <summary>
    /// Fachlicher Haupttest für den Spotify-Episodenimport.
    /// Der Test stellt sicher, dass aus der internen Spotify-Struktur
    /// eine flache und korrekt sortierte Episodenliste entsteht.
    /// </summary>
    public sealed class SpotifyEpisodeImportSourceScenarioTests
    {
        /// <summary>
        /// Stellt sicher, dass Episoden albumübergreifend
        /// nach ReleaseDate und TrackNumber sortiert werden.
        /// </summary>
        [Fact]
        public async Task GetEpisodesAsync_ReturnsEpisodesSortedByAlbumReleaseDate()
        {
            // ARRANGE
            // Der Szenario-Fake bildet eine realistische Spotify-Struktur ab.
            ServiceCollection services = new();

            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions()));

            _ = services.AddSingleton<ISpotifyApiClient>(
                new ScenarioSpotifyApiClient());

            _ = services.AddSpotifyImport();

            ServiceProvider provider = services.BuildServiceProvider();
            IEpisodeImportSource episodeImport = provider.GetRequiredService<IEpisodeImportSource>();

            // ACT
            IReadOnlyList<ImportEpisode> episodes = await episodeImport.GetEpisodesAsync("artist-ddf", cancellationToken: TestContext.Current.CancellationToken);

            // ASSERT
            // Ein Album = eine Episode – zwei Alben ergeben zwei Episoden.
            Assert.Equal(2, episodes.Count);

            // Die Episode aus dem früheren Album muss zuerst erscheinen (Sortierung nach ReleaseDate).
            // Der Titel ist jetzt der Albumname, nicht mehr der Trackname.
            Assert.Equal("Früheres Album", episodes[0].Title);
            Assert.Equal("Späteres Album", episodes[1].Title);
        }
    }
}
