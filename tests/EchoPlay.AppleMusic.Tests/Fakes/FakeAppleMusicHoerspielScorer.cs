using EchoPlay.AppleMusic.Dtos;
using EchoPlay.AppleMusic.Scoring;
using EchoPlay.Core.Scoring;

namespace EchoPlay.AppleMusic.Tests.Fakes
{
    /// <summary>
    /// Fake-Implementierung eines Hörspiel-Scorers für iTunes-Künstler.
    /// Der Fake liefert ein fest konfiguriertes Bewertungsergebnis, um fachliche Entscheidungen deterministisch testen zu können.
    /// </summary>
    /// <param name="result">Das zurückzugebende Scoring-Ergebnis.</param>
    /// <param name="artworkUrl">Die zurückzugebende Cover-Adresse.</param>
    internal sealed class FakeAppleMusicHoerspielScorer(HoerspielScoreResult result, string? artworkUrl = null)
        : IHoerspielScorer<ITunesArtistDto>, IAppleMusicArtistScorer
    {
        private readonly HoerspielScoreResult _result = result;
        private readonly string? _artworkUrl = artworkUrl;

        /// <summary>Die Suchbegriffe, mit denen der Fake aufgerufen wurde.</summary>
        public List<string> ScoredArtists { get; } = [];

        /// <summary>
        /// Führt eine fachliche Hörspiel-Bewertung durch.
        /// Die übergebenen Daten werden bewusst ignoriert, da dieser Fake ausschließlich für kontrollierte Tests gedacht ist.
        /// </summary>
        /// <param name="source">Der iTunes-Künstler.</param>
        /// <param name="searchQuery">Der ursprüngliche Suchbegriff.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Das fest konfigurierte Bewertungsergebnis.</returns>
        public Task<HoerspielScoreResult> ScoreAsync(
            ITunesArtistDto source,
            string searchQuery,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);

            ScoredArtists.Add(source.ArtistName);
            return Task.FromResult(_result);
        }

        /// <inheritdoc/>
        public async Task<AppleMusicArtistScore> ScoreArtistAsync(
            ITunesArtistDto artist,
            string searchQuery,
            CancellationToken cancellationToken = default)
        {
            HoerspielScoreResult score = await ScoreAsync(artist, searchQuery, cancellationToken);
            return new AppleMusicArtistScore(score, _artworkUrl);
        }
    }
}
