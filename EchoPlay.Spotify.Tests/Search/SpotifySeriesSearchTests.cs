using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using EchoPlay.Core.Scoring;
using EchoPlay.Spotify.Abstractions;
using EchoPlay.Spotify.DependencyInjection;
using EchoPlay.Spotify.Dtos;
using EchoPlay.Spotify.Tests.Fakes;
using EchoPlay.Spotify.Tests.TestData;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.Spotify.Tests.Search
{
    /// <summary>
    /// Fachliche Tests für die Spotify-Seriensuche.
    /// Die Tests prüfen ausschließlich vertraglich zugesichertes Verhalten und vermeiden bewusst Annahmen über interne Bewertungslogik.
    /// </summary>
    public sealed class SpotifySeriesSearchTests
    {
        /// <summary>
        /// Stellt sicher, dass ein fachlich geeigneter Spotify-Künstler die Seriensuche passiert und als Import-Serie zurückgegeben wird.
        /// </summary>
        [Fact]
        public async Task Search_KnownHoerspielArtist_ReturnsImportSeries()
        {
            // ARRANGE
            // Der DI-Container wird analog zum Produktivsystem aufgebaut, um reale Integrations- und Registrierungsfehler sichtbar zu machen.
            ServiceCollection services = new();

            // AddSpotifyImport muss vor den Fakes aufgerufen werden, damit die Fake-Registrierungen die produktiven Services überschreiben.
            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions()));
            _ = services.AddSpotifyImport();

            // Der Fake-API-Client simuliert einen bekannten Hörspiel-Künstler.
            _ = services.AddSingleton<ISpotifyApiClient>(
                new FakeSpotifyApiClient(
                    artists: [SpotifyTestData.DieDreiFragezeichen]));

            // Der Fake-Scorer liefert ein positives Ergebnis, das den Künstler als Hörspiel akzeptiert.
            _ = services.AddSingleton<IHoerspielScorer<SpotifyArtistDto>>(
                new FakeHoerspielScorer(
                    HoerspielScoreResult.Yes(
                        "artist-ddf",
                        HoerspielDecisionReason.KnownSeriesName,
                        1_000,
                        "Test: fachlich geeigneter Hörspiel-Künstler")));

            ServiceProvider provider = services.BuildServiceProvider();
            ISeriesImportSearch search = provider.GetRequiredService<ISeriesImportSearch>();

            // ACT
            IReadOnlyList<ImportSeries> result = await search.SearchAsync("Die drei ???");

            // ASSERT
            // Der Kandidat muss die Seriensuche passieren.
            _ = Assert.Single(result);

            ImportSeries series = result[0];

            // Der Titel muss korrekt aus dem Künstlernamen abgeleitet sein.
            Assert.Equal("Die drei ???", series.Title);

            // Die Serie muss eine Bewertung erhalten haben.
            Assert.True(series.Score >= 0);
        }

        /// <summary>
        /// Stellt sicher, dass ein fachlich ungeeigneter Künstler von der Seriensuche vollständig ausgeschlossen wird.
        /// </summary>
        [Fact]
        public async Task Search_NonHoerspielArtist_ReturnsNoSeries()
        {
            // ARRANGE
            // Auch für den Negativfall wird der vollständige Produktivaufbau verwendet, um sicherzustellen, dass Ablehnungslogik korrekt greift.
            ServiceCollection services = new();

            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions()));
            _ = services.AddSpotifyImport();

            // Der Fake-API-Client liefert einen Künstler, der namentlich zum Suchbegriff passt.
            _ = services.AddSingleton<ISpotifyApiClient>(
                new FakeSpotifyApiClient(
                    artists: [SpotifyTestData.UngeeigneterKuenstler]));

            // Der Fake-Scorer lehnt den Künstler ab, obwohl die API-Suche ihn findet.
            _ = services.AddSingleton<IHoerspielScorer<SpotifyArtistDto>>(
                new FakeHoerspielScorer(
                    HoerspielScoreResult.No(
                        "spotify-artist-non-hoerspiel",
                        HoerspielDecisionReason.NegativeMusicGenre,
                        0,
                        "Test: fachlich ungeeigneter Künstler")));

            ServiceProvider provider = services.BuildServiceProvider();
            ISeriesImportSearch search = provider.GetRequiredService<ISeriesImportSearch>();

            // ACT
            // Der Suchbegriff stimmt mit dem Künstlernamen überein, sodass die API ihn liefert.
            // Die Filterung muss dann über IsHoerspiel erfolgen, nicht über die API-Suche.
            IReadOnlyList<ImportSeries> result = await search.SearchAsync("Random Pop Artist");

            // ASSERT
            // Ungeeignete Kandidaten dürfen nicht als Import-Serie erscheinen.
            Assert.Empty(result);
        }
    }
}
