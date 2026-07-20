using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.DependencyInjection;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.AppleMusic.Tests.Fakes;
using EchoPlay.AppleMusic.Tests.TestData;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using EchoPlay.Core.Scoring;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.AppleMusic.Tests.Search
{
    /// <summary>
    /// Fachliche Tests für die Apple-Music-Seriensuche.
    /// Die Tests prüfen ausschließlich vertraglich zugesichertes Verhalten und vermeiden bewusst Annahmen über interne Bewertungslogik.
    /// </summary>
    public sealed class AppleMusicSeriesSearchTests
    {
        /// <summary>
        /// Stellt sicher, dass ein fachlich geeigneter iTunes-Künstler die Seriensuche passiert und als Import-Serie zurückgegeben wird.
        /// </summary>
        [Fact]
        public async Task Search_KnownHoerspielArtist_ReturnsImportSeries()
        {
            // ARRANGE
            // Der DI-Container wird analog zum Produktivsystem aufgebaut, um reale Integrations- und Registrierungsfehler sichtbar zu machen.
            ServiceCollection services = new();

            // AddAppleMusicImport muss vor den Fakes aufgerufen werden, damit die Fake-Registrierungen die produktiven Services überschreiben.
            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions()));
            _ = services.AddAppleMusicImport();

            // Der Fake-Search-Client simuliert einen bekannten Hörspiel-Künstler.
            _ = services.AddSingleton<IAppleMusicSearchClient>(
                new FakeAppleMusicSearchClient(
                    artists: [AppleMusicTestData.DieDreiFragezeichen]));

            // Der Fake-Scorer liefert ein positives Ergebnis, das den Künstler als Hörspiel akzeptiert.
            _ = services.AddSingleton<IHoerspielScorer<ITunesArtistDto>>(
                new FakeAppleMusicHoerspielScorer(
                    HoerspielScoreResult.Yes(
                        "201306317",
                        HoerspielDecisionReason.KnownSeriesName,
                        1_000,
                        "Test: fachlich geeigneter Hörspiel-Künstler")));

            ServiceProvider provider = services.BuildServiceProvider();
            ISeriesImportSearch search = provider.GetRequiredService<ISeriesImportSearch>();

            // ACT
            IReadOnlyList<ImportSeries> result = await search.SearchAsync("Die drei ???", cancellationToken: TestContext.Current.CancellationToken);

            // ASSERT
            // Der Kandidat muss die Seriensuche passieren.
            _ = Assert.Single(result);

            ImportSeries series = result[0];

            // Der Titel muss korrekt aus dem Künstlernamen abgeleitet sein.
            Assert.Equal("Die drei ???", series.Title);

            // Die Quelle muss als AppleMusic identifiziert sein.
            Assert.Equal("AppleMusic", series.Source);

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
            ServiceCollection services = new();

            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions()));
            _ = services.AddAppleMusicImport();

            // Der Fake-Search-Client liefert einen Künstler, der namentlich zum Suchbegriff passt.
            _ = services.AddSingleton<IAppleMusicSearchClient>(
                new FakeAppleMusicSearchClient(
                    artists: [AppleMusicTestData.UngeeigneterKuenstler]));

            // Der Fake-Scorer lehnt den Künstler ab, obwohl die API-Suche ihn findet.
            _ = services.AddSingleton<IHoerspielScorer<ITunesArtistDto>>(
                new FakeAppleMusicHoerspielScorer(
                    HoerspielScoreResult.No(
                        "999999999",
                        HoerspielDecisionReason.None,
                        0,
                        "Test: fachlich ungeeigneter Künstler")));

            ServiceProvider provider = services.BuildServiceProvider();
            ISeriesImportSearch search = provider.GetRequiredService<ISeriesImportSearch>();

            // ACT
            IReadOnlyList<ImportSeries> result = await search.SearchAsync("Random Pop Artist", cancellationToken: TestContext.Current.CancellationToken);

            // ASSERT
            // Ungeeignete Kandidaten dürfen nicht als Import-Serie erscheinen.
            Assert.Empty(result);
        }

        /// <summary>
        /// Stellt sicher, dass bei einer leeren Suchantwort keine Ergebnisse zurückgegeben werden.
        /// </summary>
        [Fact]
        public async Task Search_EmptyApiResponse_ReturnsEmptyList()
        {
            // ARRANGE
            ServiceCollection services = new();

            _ = services.AddSingleton<EchoPlay.Logger.Abstractions.ILoggerFactory>(
                new EchoPlay.Logger.Core.LoggerFactory([], new EchoPlay.Logger.Configuration.LoggerOptions()));
            _ = services.AddAppleMusicImport();

            // Keine Künstler im Fake → simuliert leere Suchantwort
            _ = services.AddSingleton<IAppleMusicSearchClient>(
                new FakeAppleMusicSearchClient(artists: []));

            _ = services.AddSingleton<IHoerspielScorer<ITunesArtistDto>>(
                new FakeAppleMusicHoerspielScorer(
                    HoerspielScoreResult.No("0", HoerspielDecisionReason.None, 0, "Kein Treffer")));

            ServiceProvider provider = services.BuildServiceProvider();
            ISeriesImportSearch search = provider.GetRequiredService<ISeriesImportSearch>();

            // ACT
            IReadOnlyList<ImportSeries> result = await search.SearchAsync("Nicht vorhanden", cancellationToken: TestContext.Current.CancellationToken);

            // ASSERT
            Assert.Empty(result);
        }
    }
}
