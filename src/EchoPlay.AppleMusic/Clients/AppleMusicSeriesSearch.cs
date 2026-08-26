using EchoPlay.AppleMusic.Abstractions;
using EchoPlay.AppleMusic.Dtos;
using EchoPlay.AppleMusic.Mapping;
using EchoPlay.AppleMusic.Scoring;
using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using EchoPlay.Core.Scoring;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace EchoPlay.AppleMusic.Clients
{
    /// <summary>
    /// Implementiert die fachliche Suche nach Hörspielserien über die iTunes Search API.
    /// Sucht nach Künstlern und bewertet diese mittels Scoring-Pipeline.
    /// </summary>
    /// <remarks>
    /// Die Bewertung eines Künstlers kostet Anfragen an einer Gegenstelle mit Ratenbremse.
    /// Deshalb zwei Sparmaßnahmen vor der Bewertung: Die Vorauswahl
    /// (<see cref="AppleMusicArtistPrefilter"/>) wirft aus, was weder namentlich noch vom
    /// Genre her infrage kommt, und die Zahl der teuren Prüfungen ist begrenzt. Was dabei
    /// wegfällt, steht im Protokoll — eine stille Kappung sähe aus wie ein vollständiges Ergebnis.
    /// </remarks>
    internal sealed class AppleMusicSeriesSearch : ISeriesImportSearch
    {
        private readonly IAppleMusicSearchClient _searchClient;
        private readonly IAppleMusicArtistScorer _scorer;
        private readonly AppleMusicHoerspielSettings _settings;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger;

        /// <summary>
        /// Initialisiert die Seriensuche mit Search-Client und Scorer.
        /// </summary>
        /// <param name="searchClient">Der iTunes-Search-Client für Künstler-Suche.</param>
        /// <param name="scorer">Der Hörspiel-Scorer für die Bewertung der Suchergebnisse.</param>
        /// <param name="options">Die konfigurierbaren Bewertungsregeln.</param>
        /// <param name="loggerFactory">Die Logger-Factory zur Erstellung des Loggers.</param>
        public AppleMusicSeriesSearch(
            IAppleMusicSearchClient searchClient,
            IAppleMusicArtistScorer scorer,
            IOptions<AppleMusicHoerspielSettings> options,
            EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(searchClient);
            ArgumentNullException.ThrowIfNull(scorer);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(loggerFactory);

            _searchClient = searchClient;
            _scorer = scorer;
            _settings = options.Value;
            _logger = loggerFactory.CreateLogger("AppleMusicSeriesSearch");
        }

        /// <summary>
        /// Sucht nach importierbaren Hörspielserien anhand eines freien Suchbegriffs.
        /// </summary>
        /// <param name="query">Der Suchtext.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Eine fachlich bewertete Liste importierbarer Serien.</returns>
        public async Task<IReadOnlyList<ImportSeries>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            List<ImportSeries> results = [];

            await foreach (ImportSeries series in SearchStreamAsync(query, cancellationToken).ConfigureAwait(false))
            {
                results.Add(series);
            }

            return results;
        }

        /// <summary>
        /// Sucht nach importierbaren Hörspielserien und meldet jeden Treffer, sobald er feststeht.
        /// Schlägt die Bewertung eines einzelnen Künstlers fehl, wird dieser übersprungen –
        /// die restlichen Ergebnisse bleiben unberührt. Ein Abbruch der umgebenden Operation
        /// beendet die Suche dagegen sofort und gilt nicht als Fehlschlag.
        /// </summary>
        /// <param name="query">Der Suchtext.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>Die Hörspielserien in der Reihenfolge ihrer Bewertung.</returns>
        public async IAsyncEnumerable<ImportSeries> SearchStreamAsync(
            string query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new ArgumentException("Suchbegriff darf nicht leer sein.", nameof(query));
            }

            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope("Import:AppleMusic:Search");

            _logger.Debug(() => $"Apple-Music-Seriensuche gestartet: '{query}'.");

            ITunesResponseDto<ITunesArtistDto> response =
                await _searchClient.SearchArtistsAsync(query, ct: cancellationToken).ConfigureAwait(false);

            if (response.Results.Count == 0)
            {
                _logger.Debug("Keine Künstler gefunden.");
                yield break;
            }

            AppleMusicPrefilterResult prefiltered =
                AppleMusicArtistPrefilter.SelectCandidates(response.Results, query, _settings);

            _logger.Info(
                "Vorauswahl für '{Query}': {CandidateCount} von {ArtistCount} Künstlern kommen infrage.",
                query, prefiltered.Candidates.Count, response.Results.Count);

            int deepChecks = 0;
            int skipped = 0;
            int found = 0;

            foreach (AppleMusicArtistCandidate candidate in prefiltered.Candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Bekannte Serien stehen ohne weitere Anfrage fest und zählen deshalb nicht
                // gegen das Budget der teuren Prüfungen.
                if (!candidate.IsKnownSeries)
                {
                    if (deepChecks >= _settings.MaxDeepChecks)
                    {
                        skipped++;
                        continue;
                    }

                    deepChecks++;
                }

                ImportSeries? series = await ScoreCandidateAsync(candidate.Artist, query, cancellationToken).ConfigureAwait(false);

                // Nur Kandidaten aufnehmen, die vom Scorer als Hörspiel erkannt wurden
                if (series is null || !series.IsHoerspiel)
                {
                    continue;
                }

                found++;
                yield return series;
            }

            if (skipped > 0)
            {
                _logger.Info(
                    "Vorauswahl für '{Query}': {SkippedCount} Kandidaten nach {MaxDeepChecks} Albenprüfungen nicht mehr geprüft.",
                    query, skipped, _settings.MaxDeepChecks);
            }

            _logger.Info("Apple-Music-Seriensuche abgeschlossen: {ResultCount} Hörspielserien gefunden.", found);
        }

        /// <summary>
        /// Bewertet einen Künstler und baut daraus das Serienmodell.
        /// </summary>
        /// <param name="artist">Der zu bewertende Künstler.</param>
        /// <param name="query">Der Suchtext.</param>
        /// <param name="cancellationToken">Abbruchtoken der umgebenden Operation.</param>
        /// <returns>
        /// Das Serienmodell, oder <see langword="null"/>, wenn die Bewertung fehlschlug.
        /// Ein Abbruch wird durchgereicht und beendet die Suche.
        /// </returns>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Einzelne Bewertungsfehler aus der Scoring-Pipeline dürfen die Gesamtsuche nicht abbrechen; der Scorer kombiniert mehrere Heuristiken und die konkreten Fehlertypen sind nicht vollständig vorhersehbar.")]
        private async Task<ImportSeries?> ScoreCandidateAsync(
            ITunesArtistDto artist,
            string query,
            CancellationToken cancellationToken)
        {
            AppleMusicArtistScore score;

            try
            {
                score = await _scorer.ScoreArtistAsync(artist, query, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ein Abbruch ist kein Fehlschlag: Der Nutzer tippt weiter, die Suche ist überholt.
                // Ohne diesen Zweig meldete das Protokoll für jeden verbliebenen Künstler einen
                // Ausfall, der keiner war — und verdeckte damit die echten.
                throw;
            }
            catch (Exception ex)
            {
                // Einzelne Bewertungsfehler dürfen die Gesamtsuche nicht unterbrechen.
                _logger.Warning(
                    $"Bewertung für iTunes-Künstler '{artist.ArtistId}' ({artist.ArtistName}) fehlgeschlagen. Künstler wird übersprungen.");
                _logger.Error("Fehlerdetails:", ex);
                return null;
            }

            return AppleMusicSeriesMapper.Map(artist, score.Score, score.ArtworkUrl);
        }
    }
}
