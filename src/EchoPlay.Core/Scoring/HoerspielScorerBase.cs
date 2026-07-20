using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Scoping;

namespace EchoPlay.Core.Scoring
{
    /// <summary>
    /// Gemeinsame Basis für provider-spezifische Hörspiel-Scorer. Kapselt das wiederkehrende
    /// Gerüst von <see cref="ScoreAsync"/> – Log-Scope, Cache-Lookup, Analyse-Aufruf, Cache-Store
    /// und Ergebnis-Logging. Ableitungen liefern nur die Artist-Identität, den Provider-Namen
    /// und die eigentliche Analyse-und-Bewertung.
    /// </summary>
    /// <typeparam name="TSource">Der provider-spezifische Künstler-Typ.</typeparam>
    public abstract class HoerspielScorerBase<TSource> : IHoerspielScorer<TSource>
    {
        private readonly HoerspielDecisionCache _cache;

        /// <summary>Der Logger für Scope, Debug- und Ergebnis-Ausgaben.</summary>
        protected ILogger Logger { get; }

        /// <summary>
        /// Initialisiert die Basis mit Entscheidungs-Cache und Logger.
        /// </summary>
        /// <param name="cache">Der Cache für bereits bewertete Künstler.</param>
        /// <param name="logger">Der Logger des konkreten Scorers.</param>
        protected HoerspielScorerBase(HoerspielDecisionCache cache, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(cache);
            ArgumentNullException.ThrowIfNull(logger);

            _cache = cache;
            Logger = logger;
        }

        /// <summary>Der Provider-Name für den Log-Scope (z.B. "Spotify", "AppleMusic").</summary>
        protected abstract string ProviderName { get; }

        /// <summary>Liefert die Artist-ID (Cache-Schlüssel) aus der Quelle.</summary>
        /// <param name="source">Der Künstler.</param>
        /// <returns>Die Artist-ID als String.</returns>
        protected abstract string GetArtistId(TSource source);

        /// <summary>Liefert den Anzeigenamen des Künstlers für die Logs.</summary>
        /// <param name="source">Der Künstler.</param>
        /// <returns>Der Künstlername.</returns>
        protected abstract string GetArtistName(TSource source);

        /// <summary>
        /// Führt die provider-spezifische Analyse durch und bewertet sie zum Ergebnis.
        /// </summary>
        /// <param name="source">Der Künstler.</param>
        /// <param name="artistId">Die bereits ermittelte Artist-ID.</param>
        /// <param name="searchQuery">Der ursprüngliche Suchbegriff.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Das Bewertungsergebnis.</returns>
        protected abstract Task<HoerspielScoreResult> AnalyzeAndEvaluateAsync(
            TSource source,
            string artistId,
            string searchQuery,
            CancellationToken cancellationToken);

        /// <inheritdoc/>
        public async Task<HoerspielScoreResult> ScoreAsync(
            TSource source,
            string searchQuery,
            CancellationToken cancellationToken = default)
        {
            string artistId = GetArtistId(source);

            using LogScope scope = Logger.BeginScope($"Scoring:{ProviderName}:{artistId}");

            // Cache-Prüfung: bereits bewertete Künstler nicht erneut analysieren
            if (_cache.TryGet(artistId, out HoerspielScoreResult? cached) && cached != null)
            {
                Logger.Debug(() => $"Cache-Treffer für '{GetArtistName(source)}'");
                return cached;
            }

            Logger.Debug(() => $"Starte Analyse für '{GetArtistName(source)}'");

            HoerspielScoreResult result = await AnalyzeAndEvaluateAsync(source, artistId, searchQuery, cancellationToken).ConfigureAwait(false);

            _cache.Store(result);

            Logger.Info(
                "Ergebnis für '{ArtistName}': {Classification} ({Score} Punkte)",
                GetArtistName(source), result.IsHoerspiel ? "Hörspiel" : "kein Hörspiel", result.Score);

            return result;
        }
    }
}
