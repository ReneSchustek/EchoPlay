using EchoPlay.Core.Scoring;
using EchoPlay.Spotify.Dtos;

namespace EchoPlay.Spotify.Tests.Fakes
{
    /// <summary>
    /// Fake-Implementierung eines Hörspiel-Scorers für Spotify-Künstler.
    /// Der Fake liefert ein fest konfiguriertes Bewertungsergebnis, um fachliche Entscheidungen deterministisch testen zu können.
    /// </summary>
    /// <remarks>Erstellt einen Fake-Scorer mit einem festen Score-Ergebnis.
    /// Ob ein Künstler als Hörspiel gilt, wird später anhand des Scores interpretiert (z. B. über Schwellwerte).</remarks>
    /// <param name="result">Das zurückzugebende Scoring-Ergebnis.</param>
    internal sealed class FakeHoerspielScorer(HoerspielScoreResult result) : IHoerspielScorer<SpotifyArtistDto>
    {
        private readonly HoerspielScoreResult _result = result;

        /// <summary>
        /// Führt eine fachliche Hörspiel-Bewertung durch.
        /// Die übergebenen Daten werden bewusst ignoriert, da dieser Fake ausschließlich für kontrollierte Tests gedacht ist.
        /// </summary>
        /// <param name="source">Der Spotify-Künstler.</param>
        /// <param name="searchQuery">Der ursprüngliche Suchbegriff.</param>
        /// <returns>Das fest konfigurierte Bewertungsergebnis.</returns>
        public Task<HoerspielScoreResult> ScoreAsync(
            SpotifyArtistDto source,
            string searchQuery)
        {
            return Task.FromResult(_result);
        }
    }
}
