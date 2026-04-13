using EchoPlay.AppleMusic.Dtos;
using EchoPlay.Core.Scoring;
using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Scoping;
using Microsoft.Extensions.Options;

namespace EchoPlay.AppleMusic.Scoring
{
    /// <summary>
    /// Apple-Music-spezifische Implementierung der fachlichen Hörspiel-Bewertung.
    /// Der Scorer enthält ausschließlich Arithmetik und Entscheidungslogik.
    /// Die eigentliche Analyse wird an den <see cref="AppleMusicHoerspielAnalyzer"/> delegiert.
    /// Seit dem Wechsel auf die iTunes Search API wird zusätzlich das Genre als positiver Indikator genutzt.
    /// </summary>
    internal sealed class AppleMusicHoerspielScorer : IHoerspielScorer<ITunesArtistDto>
    {
        private readonly AppleMusicHoerspielAnalyzer _analyzer;
        private readonly AppleMusicHoerspielSettings _settings;
        private readonly HoerspielDecisionCache _cache;
        private readonly ILogger _logger;

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
        {
            _analyzer = analyzer;
            _settings = options.Value;
            _cache = cache;
            _logger = loggerFactory.CreateLogger("AppleMusicHoerspielScorer");
        }

        /// <summary>
        /// Bewertet einen iTunes-Künstler asynchron hinsichtlich seiner Eignung als Hörspiel.
        /// </summary>
        /// <param name="source">Der iTunes-Künstler.</param>
        /// <param name="searchQuery">Ursprünglicher Suchbegriff.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Das Ergebnis der Hörspiel-Bewertung.</returns>
        public async Task<HoerspielScoreResult> ScoreAsync(
            ITunesArtistDto source,
            string searchQuery,
            CancellationToken cancellationToken = default)
        {
            string artistId = source.ArtistId.ToString(System.Globalization.CultureInfo.InvariantCulture);

            using LogScope scope = _logger.BeginScope($"Scoring:AppleMusic:{artistId}");

            // Cache-Prüfung: bereits bewertete Künstler nicht erneut analysieren
            if (_cache.TryGet(artistId, out HoerspielScoreResult? cached) && cached != null)
            {
                _logger.Debug($"Cache-Treffer für '{source.ArtistName}'");
                return cached;
            }

            _logger.Debug($"Starte Analyse für '{source.ArtistName}'");

            AppleMusicHoerspielAnalysis analysis = await _analyzer.AnalyzeAsync(source, searchQuery).ConfigureAwait(false);

            HoerspielScoreResult result = Evaluate(artistId, analysis);

            _cache.Store(result);

            _logger.Info($"Ergebnis für '{source.ArtistName}': {(result.IsHoerspiel ? "Hörspiel" : "kein Hörspiel")} ({result.Score} Punkte)");

            return result;
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
                _logger.Debug($"Hard-Accept: bekannte Hörspielserie");
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
