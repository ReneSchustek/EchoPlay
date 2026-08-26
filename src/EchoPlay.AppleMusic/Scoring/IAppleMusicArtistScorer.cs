using EchoPlay.AppleMusic.Dtos;
using EchoPlay.Core.Scoring;

namespace EchoPlay.AppleMusic.Scoring
{
    /// <summary>
    /// Die Bewertung eines iTunes-Künstlers samt der Cover-Adresse, die dabei anfiel.
    /// </summary>
    /// <param name="Score">Das fachliche Bewertungsergebnis.</param>
    /// <param name="ArtworkUrl">
    /// Cover-Adresse des ersten Albums, oder <see langword="null"/>, wenn die Albenprüfung
    /// übersprungen wurde oder der Künstler keine Alben hat.
    /// </param>
    internal readonly record struct AppleMusicArtistScore(HoerspielScoreResult Score, string? ArtworkUrl);

    /// <summary>
    /// Bewertet einen iTunes-Künstler und gibt dabei auch die Cover-Adresse heraus.
    /// </summary>
    /// <remarks>
    /// Der gemeinsame <see cref="IHoerspielScorer{TSource}"/> liefert genau das, was er
    /// verspricht: eine Bewertung. Die Suche braucht darüber hinaus das Artwork, das bei der
    /// Albenprüfung ohnehin durch die Hände geht — dafür dieser schmale Zusatzvertrag,
    /// statt die gemeinsame Bewertung um ein Anzeigemerkmal zu erweitern.
    /// </remarks>
    internal interface IAppleMusicArtistScorer
    {
        /// <summary>
        /// Bewertet einen Künstler.
        /// </summary>
        /// <param name="artist">Der iTunes-Künstler.</param>
        /// <param name="searchQuery">Der ursprüngliche Suchbegriff.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Bewertung und Cover-Adresse.</returns>
        Task<AppleMusicArtistScore> ScoreArtistAsync(
            ITunesArtistDto artist,
            string searchQuery,
            CancellationToken cancellationToken = default);
    }
}
