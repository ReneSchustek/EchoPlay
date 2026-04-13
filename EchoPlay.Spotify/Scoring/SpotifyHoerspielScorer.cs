using EchoPlay.Core.Scoring;
using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Scoping;
using EchoPlay.Spotify.Dtos;
using Microsoft.Extensions.Options;

namespace EchoPlay.Spotify.Scoring
{
    /// <summary>
    /// Spotify-spezifische Implementierung der fachlichen Hörspiel-Bewertung.
    /// Der Scorer enthält ausschließlich Arithmetik und Entscheidungslogik.
    /// Die eigentliche Analyse (API-Aufrufe, Heuristiken) wird an den
    /// <see cref="SpotifyHoerspielAnalyzer"/> delegiert.
    /// </summary>
    internal sealed class SpotifyHoerspielScorer : IHoerspielScorer<SpotifyArtistDto>
    {
        private readonly SpotifyHoerspielAnalyzer _analyzer;
        private readonly SpotifyHoerspielSettings _settings;
        private readonly HoerspielDecisionCache _cache;
        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert den Scorer mit Analyzer, Einstellungen, Cache und Logger.
        /// </summary>
        /// <param name="analyzer">Der Spotify-Hörspiel-Analyzer für die fachliche Analyse.</param>
        /// <param name="options">Die konfigurierbaren Bewertungsregeln.</param>
        /// <param name="cache">Der Cache für bereits bewertete Künstler.</param>
        /// <param name="loggerFactory">Factory zum Erstellen des Loggers.</param>
        public SpotifyHoerspielScorer(
            SpotifyHoerspielAnalyzer analyzer,
            IOptions<SpotifyHoerspielSettings> options,
            HoerspielDecisionCache cache,
            ILoggerFactory loggerFactory)
        {
            _analyzer = analyzer;
            _settings = options.Value;
            _cache = cache;
            _logger = loggerFactory.CreateLogger("SpotifyHoerspielScorer");
        }

        /// <summary>
        /// Bewertet einen Spotify-Künstler asynchron hinsichtlich seiner Eignung als Hörspiel.
        /// </summary>
        /// <param name="source">Der Spotify-Künstler.</param>
        /// <param name="searchQuery">Ursprünglicher Suchbegriff.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Das Ergebnis der Hörspiel-Bewertung.</returns>
        public async Task<HoerspielScoreResult> ScoreAsync(
            SpotifyArtistDto source,
            string searchQuery,
            CancellationToken cancellationToken = default)
        {
            using LogScope scope = _logger.BeginScope($"Scoring:Spotify:{source.SpotifyArtistId}");

            // Cache-Prüfung: bereits bewertete Künstler nicht erneut analysieren
            if (_cache.TryGet(source.SpotifyArtistId, out HoerspielScoreResult? cached) && cached != null)
            {
                _logger.Debug($"Cache-Treffer für '{source.Name}'");
                return cached;
            }

            _logger.Debug($"Starte Analyse für '{source.Name}'");

            SpotifyHoerspielAnalysis analysis = await _analyzer.AnalyzeAsync(source, searchQuery, cancellationToken).ConfigureAwait(false);

            HoerspielScoreResult result = Evaluate(source.SpotifyArtistId, analysis);

            _cache.Store(result);

            _logger.Info($"Ergebnis für '{source.Name}': {(result.IsHoerspiel ? "Hörspiel" : "kein Hörspiel")} ({result.Score} Punkte)");

            return result;
        }

        /// <summary>
        /// Berechnet das Bewertungsergebnis aus den Analyse-Flags.
        /// Enthält keine API-Aufrufe oder Heuristiken, nur Arithmetik.
        /// </summary>
        /// <param name="artistId">Die Spotify-Artist-ID.</param>
        /// <param name="analysis">Das Analyse-Ergebnis.</param>
        /// <returns>Das Bewertungsergebnis.</returns>
        private HoerspielScoreResult Evaluate(string artistId, SpotifyHoerspielAnalysis analysis)
        {
            // Harte Ablehnung bei negativem Musik-Genre
            if (analysis.HasNegativeMusicGenre)
            {
                _logger.Debug($"Hard-Reject: negatives Musik-Genre erkannt");
                return HoerspielScoreResult.No(
                    artistId,
                    HoerspielDecisionReason.NegativeMusicGenre,
                    0,
                    analysis.DebugInfo);
            }

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
