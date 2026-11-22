using EchoPlay.Logger.Abstractions;
using System.Diagnostics;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Protokolliert Start, ersten Treffer und Gesamtdauer einer Suche. Ohne diese drei Marken
    /// lässt sich am Protokoll nicht nachrechnen, wie lange der Nutzer auf die erste Kachel
    /// gewartet hat, und genau das ist die Zahl, auf die es hier ankommt.
    /// </summary>
    /// <param name="logger">Der Protokollkanal; ohne ihn bleibt jede Marke still.</param>
    internal sealed class SearchTimingLog(ILogger? logger)
    {
        private readonly Stopwatch _stopwatch = new();
        private string _query = string.Empty;
        private bool _firstResultReported;

        /// <summary>Beginnt die Zeitmessung für eine neue Suche.</summary>
        /// <param name="query">Der Suchbegriff.</param>
        public void Started(string query)
        {
            _query = query;
            _firstResultReported = false;
            _stopwatch.Restart();
            logger?.Info("Suche \"{Query}\" gestartet.", query);
        }

        /// <summary>Schreibt die Wartezeit bis zur ersten Kachel ins Protokoll, einmal je Suche.</summary>
        public void ResultShown()
        {
            if (_firstResultReported)
            {
                return;
            }

            _firstResultReported = true;
            logger?.Info("Suche \"{Query}\": erster Treffer nach {ElapsedMs} ms.", _query, _stopwatch.ElapsedMilliseconds);
        }

        /// <summary>Schreibt Trefferzahl und Gesamtdauer ins Protokoll.</summary>
        /// <param name="resultCount">Die Zahl der Treffer.</param>
        public void Completed(int resultCount)
        {
            logger?.Info(
                "Suche \"{Query}\" abgeschlossen: {ResultCount} Treffer nach {ElapsedMs} ms.",
                _query, resultCount, _stopwatch.ElapsedMilliseconds);
        }
    }
}
