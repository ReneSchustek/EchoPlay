using System.Globalization;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.Core.Scoring;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.Options;

namespace EchoPlay.AppleMusic.Scoring
{
    /// <summary>
    /// Apple-Music-spezifische Implementierung der fachlichen Hörspiel-Bewertung.
    /// Der Scorer enthält ausschließlich Arithmetik und Entscheidungslogik.
    /// Die eigentliche Analyse wird an den <see cref="AppleMusicHoerspielAnalyzer"/> delegiert;
    /// das Score-Gerüst (Cache, Logging) stammt aus <see cref="HoerspielScorerBase{TSource}"/>.
    /// Seit dem Wechsel auf die iTunes Search API wird zusätzlich das Genre als positiver Indikator genutzt.
    /// Thread-Safety: Alle Felder sind <c>readonly</c>, der gemeinsame <see cref="HoerspielDecisionCache"/>
    /// ist thread-safe. Instanzen dürfen parallel von mehreren Scopes genutzt werden.
    /// </summary>
    internal sealed class AppleMusicHoerspielScorer : HoerspielScorerBase<ITunesArtistDto>
    {
        private readonly AppleMusicHoerspielAnalyzer _analyzer;
        private readonly AppleMusicHoerspielSettings _settings;

        /// <summary>
        /// Initialisiert den Scorer mit Analyzer, Einstellungen, Cache und Logger.
        /// </summary>
        /// <param name="analyzer">Der Apple-Music-Hörspiel-Analyzer für die fachliche Analyse.</param>
        /// <param name="options">Die konfigurierbaren Bewertungsregeln.</param>
        /// <param name="cache">Der Cache für bereits bewertete Künstler.</param>
        /// <param name="loggerFactory">Factory zum Erstellen des Loggers.</param>
        public AppleMusicHoerspielScorer(
            AppleMusicHoerspielAnalyzer analyzer,
            IOptions<AppleMusicHoerspielSettings> options,
            HoerspielDecisionCache cache,
            ILoggerFactory loggerFactory)
            : base(cache, loggerFactory.CreateLogger("AppleMusicHoerspielScorer"))
        {
            _analyzer = analyzer;
            _settings = options.Value;
        }

        /// <inheritdoc/>
        protected override string ProviderName => "AppleMusic";

        /// <inheritdoc/>
        protected override string GetArtistId(ITunesArtistDto source) =>
            source.ArtistId.ToString(CultureInfo.InvariantCulture);

        /// <inheritdoc/>
        protected override string GetArtistName(ITunesArtistDto source) => source.ArtistName;

        /// <inheritdoc/>
        protected override async Task<HoerspielScoreResult> AnalyzeAndEvaluateAsync(
            ITunesArtistDto source,
            string artistId,
            string searchQuery,
            CancellationToken cancellationToken)
        {
            AppleMusicHoerspielAnalysis analysis = await _analyzer.AnalyzeAsync(source, searchQuery, cancellationToken).ConfigureAwait(false);
            return Evaluate(artistId, analysis);
        }

        /// <summary>
        /// Berechnet das Bewertungsergebnis aus den Analyse-Flags.
        /// Enthält keine API-Aufrufe oder Heuristiken, nur Arithmetik.
        /// </summary>
        /// <param name="artistId">Die iTunes-Artist-ID als String.</param>
        /// <param name="analysis">Das Analyse-Ergebnis.</param>
        /// <returns>Das Bewertungsergebnis.</returns>
        private HoerspielScoreResult Evaluate(string artistId, AppleMusicHoerspielAnalysis analysis)
        {
            if (analysis.IsKnownSeries)
            {
                Logger.Debug(() => "Hard-Accept: bekannte Hörspielserie");
            }

            // Apple-Music-spezifisch: Genre-Bonus wird nach dem exakten Wort-Match eingereiht
            return HoerspielScoreCalculator.Evaluate(
                artistId,
                analysis,
                _settings,
                [new HoerspielScoreComponent(analysis.HasHoerspielGenre, _settings.GenreBonus, "Hörspiel-Genre")]);
        }
    }
}
