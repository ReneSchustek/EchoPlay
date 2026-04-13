using EchoPlay.AppleMusic.Dtos;
using EchoPlay.Core.Scoring;

namespace EchoPlay.AppleMusic.Tests.Fakes
{
    /// <summary>
    /// Fake-Implementierung eines Hörspiel-Scorers für iTunes-Künstler.
    /// Der Fake liefert ein fest konfiguriertes Bewertungsergebnis, um fachliche Entscheidungen deterministisch testen zu können.
    /// </summary>
    /// <param name="result">Das zurückzugebende Scoring-Ergebnis.</param>
    internal sealed class FakeAppleMusicHoerspielScorer(HoerspielScoreResult result) : IHoerspielScorer<ITunesArtistDto>
    {
        private readonly HoerspielScoreResult _result = result;

        /// <summary>
        /// Führt eine fachliche Hörspiel-Bewertung durch.
        /// Die übergebenen Daten werden bewusst ignoriert, da dieser Fake ausschließlich für kontrollierte Tests gedacht ist.
        /// </summary>
        /// <param name="source">Der iTunes-Künstler.</param>
        /// <param name="searchQuery">Der ursprüngliche Suchbegriff.</param>
        /// <returns>Das fest konfigurierte Bewertungsergebnis.</returns>
        public Task<HoerspielScoreResult> ScoreAsync(
            ITunesArtistDto source,
            string searchQuery,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }
    }
}
