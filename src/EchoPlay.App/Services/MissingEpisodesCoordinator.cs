using EchoPlay.Core.Http;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.App.Helpers;
using EchoPlay.App.Models;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Standard-Implementierung von <see cref="IMissingEpisodesCoordinator"/>.
    /// Singleton: nutzt eigene DI-Scopes für die DataServices, hält keinen UI-State.
    /// Aktualisiert die <see cref="StatusBarViewModel"/> während längerer Prüfungen.
    /// </summary>
    public sealed class MissingEpisodesCoordinator : IMissingEpisodesCoordinator
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly StatusBarViewModel _statusBar;
        private readonly IClock _clock;
        private readonly ILogger _logger;
        private readonly LocalEpisodeGapAnalyzer _gapAnalyzer;

        /// <summary>
        /// Initialisiert den Koordinator mit den benötigten Diensten.
        /// </summary>
        /// <param name="scopeFactory">Öffnet je Prüfung einen eigenen Datenbankbereich.</param>
        /// <param name="statusBar">Zeigt Fortschritt und den vorübergehenden Online-Status.</param>
        /// <param name="clock">Zeitquelle für den Zeitpunkt des Berichts.</param>
        /// <param name="loggerFactory">Fabrik für den Protokollkanal.</param>
        public MissingEpisodesCoordinator(
            IServiceScopeFactory scopeFactory,
            StatusBarViewModel statusBar,
            IClock clock,
            ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(scopeFactory);
            ArgumentNullException.ThrowIfNull(statusBar);
            ArgumentNullException.ThrowIfNull(clock);
            ArgumentNullException.ThrowIfNull(loggerFactory);

            _scopeFactory = scopeFactory;
            _statusBar = statusBar;
            _clock = clock;
            _logger = loggerFactory.CreateLogger("MissingEpisodesCoordinator");
            _gapAnalyzer = new LocalEpisodeGapAnalyzer(_logger);
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<string>> CheckSingleSeriesAsync(
            Guid seriesId,
            string? seriesFolderPath,
            MissingEpisodesMode mode, CancellationToken cancellationToken = default)
        {
            using EchoPlay.Logger.Scoping.LogScope jobScope = _logger.BeginScope(EchoPlay.App.Logging.JobScopes.MissingEpisodes);

            if (mode == MissingEpisodesMode.Cancel)
            {
                return [];
            }

            if (string.IsNullOrWhiteSpace(seriesFolderPath) || !Directory.Exists(seriesFolderPath))
            {
                return [SafeResourceLoader.Get(
                    "MissingEpisodesNoLocalFolder", "Kein lokaler Ordner für diese Serie vorhanden.")];
            }

            // Phase 1: Dateisystem-Lücken im Thread-Pool analysieren
            List<string> result = await Task.Run(() => LocalEpisodeGapAnalyzer.Describe(seriesFolderPath));

            // Phase 2: neue Folgen, aus der Datenbank immer, live nur wenn gewünscht
            List<string> newEpisodeMessages = await AnalyzeNewEpisodesAsync(
                seriesId, seriesFolderPath, mode == MissingEpisodesMode.WithOnline, cancellationToken);
            if (newEpisodeMessages.Count > 0)
            {
                result.Add(string.Empty);
                result.AddRange(newEpisodeMessages);
            }

            return result;
        }

        /// <inheritdoc/>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <param name="mode">Steuert, ob die Prüfung online abgleicht oder nur lokal zählt.</param>
        public async Task<MissingEpisodesReport> CheckAllSeriesAsync(MissingEpisodesMode mode, CancellationToken cancellationToken = default)
        {
            if (mode == MissingEpisodesMode.Cancel)
            {
                return CreateReport([]);
            }

            bool onlineAvailable = mode == MissingEpisodesMode.WithOnline;

            if (onlineAvailable)
            {
                _statusBar.IsTemporarilyOnline = true;
            }

            try
            {
                return CreateReport(await CheckLocalSeriesAsync(onlineAvailable, cancellationToken));
            }
            finally
            {
                if (onlineAvailable)
                {
                    _statusBar.IsTemporarilyOnline = false;
                }

                _statusBar.ScanProgress.Clear();
            }
        }

        private MissingEpisodesReport CreateReport(IReadOnlyList<SeriesMissingEpisodesResult> results) => new()
        {
            CheckedAtUtc = _clock.UtcNow,
            Results = results
        };

        /// <summary>
        /// Prüft jede abonnierte Serie mit lokalem Ordner, alphabetisch, und zeigt den Fortschritt
        /// in der Statusleiste.
        /// </summary>
        private async Task<List<SeriesMissingEpisodesResult>> CheckLocalSeriesAsync(bool onlineAvailable, CancellationToken cancellationToken)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IOnlineEpisodeChecker checker = scope.ServiceProvider.GetRequiredService<IOnlineEpisodeChecker>();
            KnownNewEpisodeLookup knownEpisodes = scope.ServiceProvider.GetRequiredService<KnownNewEpisodeLookup>();
            IReadOnlyList<Series> subscribed = await scope.ServiceProvider
                .GetRequiredService<ISeriesDataService>()
                .GetSubscribedAsync(cancellationToken);

            List<Series> localSeries = [.. subscribed
                .Where(s => !string.IsNullOrWhiteSpace(s.LocalFolderPath))
                .OrderBy(s => s.Title)];

            // Dasselbe Muster nutzt der Neuerscheinungs-Abgleich in der Online-Mediathek.
            string progressPattern = SafeResourceLoader.Get(
                "OnlineRefreshProgressText", "Prüfe Serie {0}/{1}: {2} \u2026");

            List<SeriesMissingEpisodesResult> results = new(localSeries.Count);
            for (int i = 0; i < localSeries.Count; i++)
            {
                Series series = localSeries[i];
                _statusBar.ScanProgress.SetText(string.Format(
                    CultureInfo.CurrentCulture, progressPattern, i + 1, localSeries.Count, series.Title));

                results.Add(await CheckSingleSeriesForReportAsync(
                    series, onlineAvailable ? checker : null, knownEpisodes, cancellationToken));
            }

            return results;
        }

        /// <summary>
        /// Neue Folgen nach der letzten lokalen Nummer für den Einzelbericht: die beim Anbieter
        /// bekannten aus der Datenbank, mit <paramref name="includeLive"/> zusätzlich der
        /// Live-Abgleich. Während des Live-Abgleichs zeigt die Statusleiste den temporären
        /// Online-Status (Nutzer hat im Dialog bereits zugestimmt).
        /// </summary>
        /// <param name="seriesId">Die geprüfte Serie.</param>
        /// <param name="seriesFolderPath">Ihr lokaler Ordner.</param>
        /// <param name="includeLive">Ob zusätzlich live beim Anbieter abgefragt wird.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Neue Folgen sind eine Ergänzung zur lokalen Lückensuche: Datenbank- oder Provider-Fehler (iTunes-Search-API, HTTP-Timeout) dürfen den Dialog nicht reißen; der StatusBar-Flag wird im finally zurückgesetzt und die Liste bleibt im Fehlerfall leer.")]
        private async Task<List<string>> AnalyzeNewEpisodesAsync(
            Guid seriesId, string seriesFolderPath, bool includeLive, CancellationToken cancellationToken = default)
        {
            _statusBar.IsTemporarilyOnline = includeLive;

            try
            {
                return await CollectNewEpisodesAsync(seriesId, seriesFolderPath, includeLive, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ein Abbruch des Aufrufers ist kein Fehlschlag und gehört nach oben.
                throw;
            }
            catch (Exception ex)
            {
                // Ohne diesen Teil zeigt der Dialog eben nur die lokalen Lücken.
                _logger.Warning("Abgleich neuer Folgen fehlgeschlagen: {Reason}", ex.Message);
                return [];
            }
            finally
            {
                _statusBar.IsTemporarilyOnline = false;
            }
        }

        /// <summary>
        /// Sucht die Folgen nach der höchsten lokalen Nummer, in der Datenbank und auf Wunsch live,
        /// und liefert sie als Zeilen für den Dialog.
        /// </summary>
        private async Task<List<string>> CollectNewEpisodesAsync(
            Guid seriesId, string seriesFolderPath, bool includeLive, CancellationToken cancellationToken)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            Series? series = await seriesService.GetByIdAsync(seriesId, cancellationToken);
            int localHighest = await Task.Run(() => OnlineEpisodeChecker.GetLocalHighestEpisodeNumber(seriesFolderPath, _logger));

            if (series is null || localHighest == 0)
            {
                return [];
            }

            IReadOnlyList<OnlineEpisodeInfo> known = await scope.ServiceProvider
                .GetRequiredService<KnownNewEpisodeLookup>()
                .FindAfterAsync(series, localHighest, cancellationToken);
            IReadOnlyList<OnlineEpisodeInfo> live = includeLive
                ? (await CheckLiveAsync(series, seriesFolderPath,
                    scope.ServiceProvider.GetRequiredService<IOnlineEpisodeChecker>(), cancellationToken)).Episodes
                : [];

            return FormatNewEpisodes(localHighest, KnownNewEpisodeLookup.Merge(known, live));
        }

        /// <summary>
        /// Fragt beim Anbieter live ab, welche Folgen nach der letzten lokalen Nummer erschienen sind.
        /// </summary>
        /// <param name="series">Die lokale Serie.</param>
        /// <param name="seriesFolderPath">Ihr lokaler Ordner, aus dem die höchste Nummer gelesen wird.</param>
        /// <param name="checker">Der Live-Abgleich.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Die neuen Folgen und die höchste Nummer beim Anbieter; leer und 0 ohne Ergebnis.</returns>
        private static async Task<(IReadOnlyList<OnlineEpisodeInfo> Episodes, int OnlineHighest)> CheckLiveAsync(
            Series series, string seriesFolderPath, IOnlineEpisodeChecker checker, CancellationToken cancellationToken)
        {
            CheckableSeriesInfo checkable = new()
            {
                SeriesId = series.Id,
                Title = series.Title,
                AppleMusicArtistId = series.AppleMusicArtistId,
                LocalFolderPath = seriesFolderPath,
                CoverImageUrl = WebAddress.TryParse(series.CoverImageUrl)
            };

            IReadOnlyList<OnlineEpisodeCheckResult> results = await checker.CheckAllAsync([checkable], cancellationToken);
            if (results.Count == 0)
            {
                return ([], 0);
            }

            List<OnlineEpisodeInfo> episodes = results[0].MissingOnlineEpisodes
                .Select(episode => new OnlineEpisodeInfo { EpisodeNumber = episode.EpisodeNumber, Title = episode.AlbumTitle })
                .ToList();
            return (episodes, results[0].OnlineHighestNumber);
        }

        /// <summary>Schreibt die neuen Folgen als Zeilen für den Einzelbericht.</summary>
        /// <param name="localHighest">Höchste lokale Nummer, sie steht in der Überschrift.</param>
        /// <param name="episodes">Die neuen Folgen, aufsteigend.</param>
        private static List<string> FormatNewEpisodes(int localHighest, IReadOnlyList<OnlineEpisodeInfo> episodes)
        {
            if (episodes.Count == 0)
            {
                return [];
            }

            // Muster erst in eine Variable: mit dem Aufruf direkt im string.Format
            // verlangt der Analyzer ein zwischengespeichertes CompositeFormat, was bei
            // einem zur Laufzeit wechselnden Sprachtext nichts brächte.
            string headerPattern = SafeResourceLoader.Get(
                "MissingEpisodesOnlineHeader", "Online verfügbar (nach Folge {0}):");
            string entryPattern = SafeResourceLoader.Get(
                "MissingEpisodesOnlineEntry", "  Folge {0} \u2013 {1}");

            List<string> messages =
            [
                string.Format(CultureInfo.CurrentCulture, headerPattern, localHighest),
                string.Empty
            ];

            foreach (OnlineEpisodeInfo episode in episodes)
            {
                messages.Add(string.Format(
                    CultureInfo.CurrentCulture,
                    entryPattern,
                    episode.EpisodeNumber.ToString("D3", CultureInfo.CurrentCulture),
                    episode.Title));
            }

            return messages;
        }

        /// <summary>
        /// Prüft eine einzelne Serie für den Gesamtbericht: lokale Lücken, die beim Anbieter
        /// schon bekannten neuen Folgen und, wenn <paramref name="liveChecker"/> gesetzt ist, der
        /// Live-Abgleich. Fehler werden als <see cref="SeriesMissingEpisodesResult.ErrorMessage"/>
        /// im Bericht weitergereicht, ohne die gesamte Prüfung zu stoppen.
        /// </summary>
        /// <param name="series">Die lokale Serie.</param>
        /// <param name="liveChecker">Live-Abgleich; null bei „Nur offline".</param>
        /// <param name="knownEpisodes">Suche nach den neuen Folgen in der Datenbank.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Pro-Serie-Check für den Gesamtbericht: HTTP-/iTunes-Fehler oder DB-Fehler einer einzelnen Serie werden als 'ErrorMessage' im Report weitergereicht, damit die Bericht-Schleife für die übrigen Serien weiterläuft.")]
        private async Task<SeriesMissingEpisodesResult> CheckSingleSeriesForReportAsync(
            Series series, IOnlineEpisodeChecker? liveChecker, KnownNewEpisodeLookup knownEpisodes,
            CancellationToken cancellationToken = default)
        {
            try
            {
                (List<int> gaps, int localHighest) = await AnalyzeLocalFolderAsync(series);

                // Ohne erkannte Nummer gibt es kein „danach": Jede Folge des Anbieters wäre neu.
                (IReadOnlyList<OnlineEpisodeInfo> onlineEpisodes, int onlineHighest) = localHighest > 0
                    ? await FindOnlineAfterAsync(series, localHighest, liveChecker, knownEpisodes, cancellationToken)
                    : ([], 0);

                return new SeriesMissingEpisodesResult
                {
                    SeriesTitle = series.Title,
                    LocalHighestNumber = localHighest,
                    OnlineHighestNumber = onlineHighest,
                    LocalGaps = gaps,
                    OnlineEpisodes = onlineEpisodes
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ein Abbruch des Aufrufers ist kein Fehlschlag und gehört nach oben.
                throw;
            }
            catch (Exception ex)
            {
                return FailedResult(series, ex.Message);
            }
        }

        /// <summary>Der Berichtseintrag einer Serie, deren Prüfung scheiterte.</summary>
        private static SeriesMissingEpisodesResult FailedResult(Series series, string reason) => new()
        {
            SeriesTitle = series.Title,
            LocalGaps = [],
            OnlineEpisodes = [],
            ErrorMessage = reason
        };

        /// <summary>
        /// Lücken und höchste Nummer im Serienordner; ohne vorhandenen Ordner beides leer.
        /// </summary>
        private async Task<(List<int> Gaps, int Highest)> AnalyzeLocalFolderAsync(Series series)
        {
            string? folder = series.LocalFolderPath;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return ([], 0);
            }

            return await Task.Run(() => _gapAnalyzer.AnalyzeForReport(folder));
        }

        /// <summary>
        /// Die Folgen des Anbieters nach der letzten lokalen Nummer: die bekannten aus der Datenbank,
        /// mit <paramref name="liveChecker"/> zusätzlich die aus dem Live-Abgleich.
        /// </summary>
        /// <returns>Die Folgen und die höchste Nummer beim Anbieter.</returns>
        private static async Task<(IReadOnlyList<OnlineEpisodeInfo> Episodes, int Highest)> FindOnlineAfterAsync(
            Series series, int localHighest, IOnlineEpisodeChecker? liveChecker, KnownNewEpisodeLookup knownEpisodes,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<OnlineEpisodeInfo> known = await knownEpisodes.FindAfterAsync(series, localHighest, cancellationToken);
            (IReadOnlyList<OnlineEpisodeInfo> live, int liveHighest) = liveChecker is null
                ? ([], 0)
                : await CheckLiveAsync(series, series.LocalFolderPath!, liveChecker, cancellationToken); // localHighest > 0 setzt den Ordner voraus

            IReadOnlyList<OnlineEpisodeInfo> merged = KnownNewEpisodeLookup.Merge(known, live);
            return (merged, Math.Max(liveHighest, merged.Count > 0 ? merged[^1].EpisodeNumber : 0));
        }
    }
}
