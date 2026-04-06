using System.Collections.Concurrent;

namespace EchoPlay.Core.Scoring
{
    /// <summary>
    /// Thread-sicherer Cache für Hörspiel-Bewertungsergebnisse.
    /// Verhindert redundante Bewertungen, wenn derselbe Künstler mehrfach bewertet wird.
    /// Der Cache ist anbieterunabhängig und kann von Spotify, Apple Music und weiteren Quellen genutzt werden.
    /// </summary>
    public sealed class HoerspielDecisionCache
    {
        private readonly ConcurrentDictionary<string, HoerspielScoreResult> _cache = new();
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger;

        /// <summary>
        /// Initialisiert einen neuen Cache.
        /// </summary>
        /// <param name="loggerFactory">Die Logger-Factory zur Erstellung des Loggers.</param>
        public HoerspielDecisionCache(EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger("HoerspielDecisionCache");
        }

        /// <summary>
        /// Versucht, ein gecachtes Ergebnis für die angegebene Artist-ID abzurufen.
        /// </summary>
        /// <param name="artistId">Die Artist-ID.</param>
        /// <param name="result">Das gecachte Ergebnis, falls vorhanden.</param>
        /// <returns><c>true</c>, wenn ein Ergebnis im Cache gefunden wurde.</returns>
        public bool TryGet(string artistId, out HoerspielScoreResult? result)
        {
            bool found = _cache.TryGetValue(artistId, out result);

            if (found)
            {
                _logger.Debug($"Cache-Treffer für Künstler '{artistId}'.");
            }

            return found;
        }

        /// <summary>
        /// Speichert ein Bewertungsergebnis im Cache.
        /// </summary>
        /// <param name="result">Das zu cachende Ergebnis. Die Artist-ID wird aus dem Ergebnis gelesen.</param>
        public void Store(HoerspielScoreResult result)
        {
            // TryAdd schlägt lautlos fehl, wenn der Key bereits existiert.
            // Das ist gewollt – der erste Scorer gewinnt. Wir unterscheiden im Log,
            // damit Diagnosedaten nicht lügen.
            bool wasAdded = _cache.TryAdd(result.ArtistId, result);

            if (wasAdded)
            {
                _logger.Debug($"Bewertung für Künstler '{result.ArtistId}' im Cache gespeichert.");
            }
            else
            {
                _logger.Debug($"Bewertung für Künstler '{result.ArtistId}' bereits im Cache – übersprungen.");
            }
        }
    }
}
