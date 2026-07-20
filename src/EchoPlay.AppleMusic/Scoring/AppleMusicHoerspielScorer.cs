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
            AppleMusicHoerspielAnalysis analysis = await _analyzer.AnalyzeAsync(source, searchQuery).ConfigureAwait(false);
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
            // Harte Akzeptanz bei bekannter Hörspielserie
            if (analysis.IsKnownSeries)
            {
                Logger.Debug(() => "Hard-Accept: bekannte Hörspielserie");
                return HoerspielScoreResult.Yes(
                    artistId,
                    HoerspielDecisionReason.KnownSeriesName,
                    100,
                    analysis.DebugInfo);
            }

            // Score-basierte Bewertung
            int score = 0;
            List<string> scoreParts = [];

            if (analysis.NameContainsQuery)
            {
                score += _settings.NameContainsBonus;
                scoreParts.Add($"Name-Contains: +{_settings.NameContainsBonus}");
            }

            if (analysis.HasNumberVariantMatch)
            {
                score += _settings.NameContainsBonus;
                scoreParts.Add($"Zahlwort-Variante: +{_settings.NameContainsBonus}");
            }

            if (analysis.HasExactWordMatch)
            {
                score += _settings.ExactWordMatchBonus;
                scoreParts.Add($"Exaktes Wort-Match: +{_settings.ExactWordMatchBonus}");
            }

            if (analysis.HasHoerspielGenre)
            {
                score += _settings.GenreBonus;
                scoreParts.Add($"Hörspiel-Genre: +{_settings.GenreBonus}");
            }

            if (analysis.HasHoerspielAlbumStructure)
            {
                score += _settings.AlbumStructureBonus;
                scoreParts.Add($"Album-Struktur: +{_settings.AlbumStructureBonus}");
            }
            else
            {
                score += _settings.NoAlbumPenalty;
                scoreParts.Add($"Keine Hörspiel-Alben: {_settings.NoAlbumPenalty}");
            }

            string debugInfo = scoreParts.Count > 0
                ? string.Join("; ", scoreParts) + $" → Gesamt: {score}"
                : $"Keine Indikatoren gefunden → Gesamt: {score}";

            bool isHoerspiel = score >= _settings.MinimumScoreThreshold;

            if (isHoerspiel)
            {
                return HoerspielScoreResult.Yes(
                    artistId,
                    HoerspielDecisionReason.None,
                    score,
                    debugInfo);
            }

            return HoerspielScoreResult.No(
                artistId,
                HoerspielDecisionReason.None,
                score,
                debugInfo);
        }
    }
}
