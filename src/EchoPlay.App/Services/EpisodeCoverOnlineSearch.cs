using EchoPlay.Core.Scoring;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Dritte Stufe der Folgen-Cover: die Online-Suchkette. Fragt mit abnehmend genauen
    /// Suchbegriffen, nimmt nur Treffer über der Relevanzschwelle und lädt den ersten davon.
    /// </summary>
    internal sealed class EpisodeCoverOnlineSearch
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ICoverDownloader _coverDownloader;
        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert die Suche mit den Diensten des Cover-Zwischenspeichers.
        /// </summary>
        /// <param name="scopeFactory">Fabrik für den Bereich, aus dem der Suchdienst kommt.</param>
        /// <param name="coverDownloader">Lädt die Bilddaten des gewählten Treffers.</param>
        /// <param name="logger">Protokolliert gescheiterte Suchanfragen.</param>
        public EpisodeCoverOnlineSearch(IServiceScopeFactory scopeFactory, ICoverDownloader coverDownloader, ILogger logger)
        {
            _scopeFactory = scopeFactory;
            _coverDownloader = coverDownloader;
            _logger = logger;
        }

        /// <summary>
        /// Durchsucht die Cover-Dienste mit abnehmender Spezifität.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Online-Cover-Suche (CompositeCoverSearchService): HTTP-, Rate-Limit- oder Parser-Fehler der externen Quellen (iTunes/Cover Art Archive/MusicBrainz) werden zu 'null' normalisiert, sodass die aufrufende Folge 'kein Cover' bekommt.")]
        internal async Task<byte[]?> FindCoverAsync(
            string seriesName, string title, int? episodeNumber, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(seriesName)) return null;

            using IServiceScope scope = _scopeFactory.CreateScope();
            ICoverSearchService? coverSearch = scope.ServiceProvider.GetService<ICoverSearchService>();

            if (coverSearch is null) return null;

            string shortTitle = ExtractShortTitle(title, seriesName);

            foreach (string query in SearchQueries(seriesName, shortTitle, episodeNumber))
            {
                try
                {
                    IReadOnlyList<CoverSearchResult> results = await coverSearch.SearchAsync(query, cancellationToken);

                    // Ergebnisse nach Relevanz filtern. Das verhindert irrelevante Cover
                    // (z.B. „Mimi Rutherford" bei Suche nach „Die drei ??? Kids")
                    CoverSearchResult? best = FindBestMatch(
                        results, seriesName, episodeNumber, shortTitle);

                    if (best is not null)
                    {
                        byte[]? bytes = await _coverDownloader.DownloadAsync(best.FullUrl, cancellationToken);
                        if (bytes is not null) return bytes;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Ein Abbruch des Aufrufers ist kein Fehlschlag und gehört nach oben.
                    throw;
                }
                catch (Exception ex)
                {
                    // Ein Suchfehler betrifft nur diese Anfrage; die nächste Formulierung kommt dran.
                    _logger.Debug(() => $"Cover-Suche \"{query}\" fehlgeschlagen: {ex.Message}");
                }
            }

            return null;
        }

        /// <summary>Die Suchbegriffe für ein Folgen-Cover, mit abnehmender Spezifität.</summary>
        internal static List<string> SearchQueries(string seriesName, string shortTitle, int? episodeNumber)
        {
            List<string> queries = [];
            bool hasTitle = !string.IsNullOrWhiteSpace(shortTitle);
            if (episodeNumber.HasValue && hasTitle)
            {
                queries.Add($"{seriesName} Episode {episodeNumber.Value} {shortTitle}");
                queries.Add($"{seriesName} {episodeNumber.Value} {shortTitle}");
            }

            if (hasTitle)
            {
                queries.Add($"{seriesName} {shortTitle}");
            }

            if (episodeNumber.HasValue)
            {
                queries.Add($"{seriesName} {episodeNumber.Value}");
            }

            return queries;
        }

        /// <summary>
        /// Extrahiert den kurzen Folgentitel ohne Seriennamen-Präfix und Folgennummer.
        /// </summary>
        /// <param name="fullTitle">Vollständiger Folgentitel, meist mit Seriennamen und Nummer davor.</param>
        /// <param name="seriesName">Name der Serie, der aus dem Titel entfernt wird.</param>
        private static string ExtractShortTitle(string fullTitle, string seriesName)
        {
            string title = fullTitle;

            if (title.StartsWith(seriesName, StringComparison.OrdinalIgnoreCase))
            {
                title = title[seriesName.Length..].TrimStart(' ', '-', '–', ':');
            }

            // Eine Folgennummer vor dem Strich ist kurz; ein Strich weiter hinten gehört zum Titel.
            int dashIndex = title.IndexOf(" - ", StringComparison.Ordinal);
            if (dashIndex >= 0 && dashIndex < 10)
            {
                string beforeDash = title[..dashIndex].Trim();
                if (int.TryParse(beforeDash, out _))
                {
                    title = title[(dashIndex + 3)..].Trim();
                }
            }

            return title;
        }

        /// <summary>
        /// Wählt das relevanteste Suchergebnis anhand des <see cref="CoverRelevanceScorer"/>.
        /// Ergebnisse unter der Mindest-Schwelle werden verworfen.
        /// </summary>
        private static CoverSearchResult? FindBestMatch(
            IReadOnlyList<CoverSearchResult> results,
            string seriesName,
            int? episodeNumber,
            string? episodeTitle)
        {
            CoverSearchResult? best = null;
            int bestScore = 0;

            foreach (CoverSearchResult result in results)
            {
                int score = CoverRelevanceScorer.CalculateScore(
                    result.ReleaseTitle, seriesName, episodeNumber, episodeTitle);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = result;
                }
            }

            // Nur Treffer über der Mindest-Schwelle zurückgeben
            return bestScore >= CoverRelevanceScorer.MinimumThreshold ? best : null;
        }
    }
}
