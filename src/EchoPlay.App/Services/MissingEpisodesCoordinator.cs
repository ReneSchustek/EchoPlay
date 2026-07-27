using EchoPlay.Core.Abstractions.Time;
using EchoPlay.App.Models;
using EchoPlay.App.ViewModels;
using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Parsing;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        /// <summary>
        /// Initialisiert den Koordinator mit den benötigten Diensten.
        /// </summary>
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
                return ["Kein lokaler Ordner für diese Serie vorhanden."];
            }

            // Phase 1: Dateisystem-Lücken im Thread-Pool analysieren
            List<string> result = await Task.Run(() => AnalyzeMissingEpisodes(seriesFolderPath));

            // Phase 2: Online-Abgleich nur wenn gewünscht
            if (mode == MissingEpisodesMode.WithOnline)
            {
                List<string> onlineMessages = await AnalyzeLiveOnlineMissingAsync(seriesId, seriesFolderPath, cancellationToken);
                if (onlineMessages.Count > 0)
                {
                    result.Add(string.Empty);
                    result.AddRange(onlineMessages);
                }
            }

            return result;
        }

        /// <inheritdoc/>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <param name="mode">Parameter <c>mode</c>.</param>
        public async Task<MissingEpisodesReport> CheckAllSeriesAsync(MissingEpisodesMode mode, CancellationToken cancellationToken = default)
        {
            if (mode == MissingEpisodesMode.Cancel)
            {
                return new MissingEpisodesReport
                {
                    CheckedAtUtc = _clock.UtcNow,
                    Results = []
                };
            }

            bool onlineAvailable = mode == MissingEpisodesMode.WithOnline;

            if (onlineAvailable)
            {
                _statusBar.IsTemporarilyOnline = true;
            }

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                ISeriesDataService seriesService = scope.ServiceProvider
                    .GetRequiredService<ISeriesDataService>();
                IOnlineEpisodeChecker checker = scope.ServiceProvider
                    .GetRequiredService<IOnlineEpisodeChecker>();

                IReadOnlyList<Series> subscribed = await seriesService.GetSubscribedAsync(cancellationToken);

                List<Series> localSeries = subscribed
                    .Where(s => !string.IsNullOrWhiteSpace(s.LocalFolderPath))
                    .OrderBy(s => s.Title)
                    .ToList();

                List<SeriesMissingEpisodesResult> results = new(localSeries.Count);

                for (int i = 0; i < localSeries.Count; i++)
                {
                    Series series = localSeries[i];
                    _statusBar.SetScanProgress($"Prüfe Serie {i + 1}/{localSeries.Count}: {series.Title} \u2026");

                    SeriesMissingEpisodesResult result = await CheckSingleSeriesForReportAsync(series, onlineAvailable, checker, cancellationToken);
                    results.Add(result);
                }

                return new MissingEpisodesReport
                {
                    CheckedAtUtc = _clock.UtcNow,
                    Results = results
                };
            }
            finally
            {
                if (onlineAvailable)
                {
                    _statusBar.IsTemporarilyOnline = false;
                }

                _statusBar.ClearScanProgress();
            }
        }

        /// <summary>
        /// Live-Online-Abgleich per iTunes für die übergebene Serie. Setzt während
        /// der Prüfung den temporären Online-Status (Nutzer hat im Dialog bereits zugestimmt).
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Live-Online-Abgleich: Provider-Fehler (iTunes-Search-API, HTTP-Timeout) dürfen den Dialog-Flow nicht reißen; der StatusBar-Flag wird im finally zurückgesetzt und die Ergebnisliste bleibt im Fehlerfall leer.")]
        private async Task<List<string>> AnalyzeLiveOnlineMissingAsync(
            Guid seriesId,
            string seriesFolderPath, CancellationToken cancellationToken = default)
        {
            _statusBar.IsTemporarilyOnline = true;

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
                IOnlineEpisodeChecker checker = scope.ServiceProvider.GetRequiredService<IOnlineEpisodeChecker>();
                Series? series = await seriesService.GetByIdAsync(seriesId, cancellationToken);

                if (series is null)
                {
                    return [];
                }

                CheckableSeriesInfo checkable = new()
                {
                    SeriesId = series.Id,
                    Title = series.Title,
                    AppleMusicArtistId = series.AppleMusicArtistId,
                    LocalFolderPath = seriesFolderPath,
                    CoverImageUrl = series.CoverImageUrl
                };

                IReadOnlyList<OnlineEpisodeCheckResult> results =
                    await checker.CheckAllAsync([checkable], cancellationToken);

                if (results.Count == 0)
                {
                    return [];
                }

                OnlineEpisodeCheckResult checkResult = results[0];

                if (checkResult.MissingOnlineEpisodes.Count == 0)
                {
                    return [];
                }

                List<string> messages =
                [
                    $"Online verfügbar (nach Folge {checkResult.LocalHighestNumber}):",
                    string.Empty
                ];

                foreach (MissingOnlineEpisode ep in checkResult.MissingOnlineEpisodes)
                {
                    messages.Add($"  Folge {ep.EpisodeNumber:D3} \u2013 {ep.AlbumTitle}");
                }

                return messages;
            }
            catch (Exception)
            {
                return [];
            }
            finally
            {
                _statusBar.IsTemporarilyOnline = false;
            }
        }

        /// <summary>
        /// Prüft eine einzelne Serie für den Gesamtbericht: lokale Lücken plus optionaler
        /// Online-Abgleich. Fehler werden als <see cref="SeriesMissingEpisodesResult.ErrorMessage"/>
        /// im Bericht weitergereicht, ohne die gesamte Prüfung zu stoppen.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Pro-Serie-Check für den Gesamtbericht: HTTP-/iTunes-Fehler oder DB-Fehler einer einzelnen Serie werden als 'ErrorMessage' im Report weitergereicht, damit die Bericht-Schleife für die übrigen Serien weiterläuft.")]
        private async Task<SeriesMissingEpisodesResult> CheckSingleSeriesForReportAsync(
            Series series, bool onlineAvailable, IOnlineEpisodeChecker checker, CancellationToken cancellationToken = default)
        {
            try
            {
                List<int> gaps = [];
                int localHighest = 0;

                if (!string.IsNullOrWhiteSpace(series.LocalFolderPath)
                    && Directory.Exists(series.LocalFolderPath))
                {
                    (gaps, localHighest) = await Task.Run(
                        () => AnalyzeMissingEpisodesForReport(series.LocalFolderPath));
                }

                int onlineHighest = 0;
                List<OnlineEpisodeInfo> onlineEpisodes = [];

                if (onlineAvailable && localHighest > 0)
                {
                    CheckableSeriesInfo checkable = new()
                    {
                        SeriesId = series.Id,
                        Title = series.Title,
                        AppleMusicArtistId = series.AppleMusicArtistId,
                        LocalFolderPath = series.LocalFolderPath,
                        CoverImageUrl = series.CoverImageUrl
                    };

                    IReadOnlyList<OnlineEpisodeCheckResult> checkResults =
                        await checker.CheckAllAsync([checkable], cancellationToken);

                    if (checkResults.Count > 0)
                    {
                        OnlineEpisodeCheckResult cr = checkResults[0];
                        onlineHighest = cr.OnlineHighestNumber;

                        foreach (MissingOnlineEpisode ep in cr.MissingOnlineEpisodes)
                        {
                            onlineEpisodes.Add(new OnlineEpisodeInfo
                            {
                                EpisodeNumber = ep.EpisodeNumber,
                                Title = ep.AlbumTitle
                            });
                        }
                    }
                }

                return new SeriesMissingEpisodesResult
                {
                    SeriesTitle = series.Title,
                    LocalHighestNumber = localHighest,
                    OnlineHighestNumber = onlineHighest,
                    LocalGaps = gaps,
                    OnlineEpisodes = onlineEpisodes
                };
            }
            catch (Exception ex)
            {
                return new SeriesMissingEpisodesResult
                {
                    SeriesTitle = series.Title,
                    LocalGaps = [],
                    OnlineEpisodes = [],
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Analysiert lokale Lücken und liefert sowohl die fehlenden Nummern als auch
        /// die höchste gefundene Nummer. Strukturierte Variante für den Gesamtbericht.
        /// Läuft im Thread-Pool – darf keine UI-Elemente anfassen.
        /// </summary>
        /// <param name="seriesFolderPath">Absoluter Pfad des Serienordners.</param>
        private static (List<int> Gaps, int MaxNumber) AnalyzeMissingEpisodesForReport(string seriesFolderPath)
        {
            string[] subfolders;
            try
            {
                subfolders = Directory.GetDirectories(seriesFolderPath);
            }
            catch (IOException)
            {
                // Serienordner nicht lesbar – kein Bericht möglich
                return ([], 0);
            }
            catch (UnauthorizedAccessException)
            {
                // Keine Leserechte – kein Bericht möglich
                return ([], 0);
            }

            List<string> episodeFolderNames = [];
            foreach (string folder in subfolders)
            {
                try
                {
                    bool hasAudio = Directory
                        .GetFiles(folder, "*.*", SearchOption.AllDirectories)
                        .Any(EchoPlay.Core.AudioExtensions.IsAudioFile);

                    if (hasAudio)
                    {
                        string? name = Path.GetFileName(folder);
                        if (name is not null)
                        {
                            episodeFolderNames.Add(name);
                        }
                    }
                }
                catch (IOException) { /* Einzelner Ordner nicht lesbar – Rest weiterscannen */ }
                catch (UnauthorizedAccessException) { /* Kein Zugriff – überspringen */ }
            }

            if (episodeFolderNames.Count == 0) return ([], 0);

            IReadOnlyList<int> numbers = LocalEpisodeNumbers.Scan(episodeFolderNames);
            if (numbers.Count == 0) return ([], 0);

            return ([.. LocalEpisodeNumbers.FindGaps(numbers)], numbers[^1]);
        }

        /// <summary>
        /// Analysiert den Serienordner auf fehlende Folgen und gibt Anzeige-Meldungen zurück.
        /// Wird für die Einzelserien-Prüfung verwendet (formatierter Text für den Dialog).
        /// </summary>
        /// <param name="seriesFolderPath">Parameter <c>seriesFolderPath</c>.</param>
        private static List<string> AnalyzeMissingEpisodes(string seriesFolderPath)
        {
            string[] subfolders;
            try
            {
                subfolders = Directory.GetDirectories(seriesFolderPath);
            }
            catch (IOException)
            {
                return ["Ordner konnte nicht gelesen werden."];
            }
            catch (UnauthorizedAccessException)
            {
                return ["Zugriff auf den Ordner verweigert."];
            }

            // Nur Ordner mit mindestens einer Audiodatei sind echte Folgen.
            // Jubiläumsfolgen können Audio in Unterordnern (CD1, Teil A) ablegen,
            // deshalb wird rekursiv gesucht (SearchOption.AllDirectories).
            List<string> episodeFolderNames = [];
            foreach (string folder in subfolders)
            {
                try
                {
                    bool hasAudio = Directory
                        .GetFiles(folder, "*.*", SearchOption.AllDirectories)
                        .Any(EchoPlay.Core.AudioExtensions.IsAudioFile);

                    if (hasAudio)
                    {
                        string? name = Path.GetFileName(folder);
                        if (name is not null)
                        {
                            episodeFolderNames.Add(name);
                        }
                    }
                }
                catch (IOException) { /* Einzelner Ordner nicht lesbar – Rest weiterscannen */ }
                catch (UnauthorizedAccessException) { /* Kein Zugriff – überspringen */ }
            }

            if (episodeFolderNames.Count == 0)
            {
                return ["Keine Folgenordner mit Audiodateien gefunden."];
            }

            IReadOnlyList<int> numbers = LocalEpisodeNumbers.Scan(episodeFolderNames);
            if (numbers.Count == 0)
            {
                return [$"{episodeFolderNames.Count} Folgen vorhanden (keine Nummerierung erkannt)."];
            }

            IReadOnlyList<int> gaps = LocalEpisodeNumbers.FindGaps(numbers);
            int minNumber = numbers[0];
            int maxNumber = numbers[^1];

            if (gaps.Count == 0)
            {
                return [$"Alle Folgen vorhanden ({minNumber}–{maxNumber}), keine Lücken."];
            }

            List<string> messages =
            [
                $"{gaps.Count} fehlende Folge(n) im Bereich {minNumber}–{maxNumber}:",
                string.Empty
            ];

            foreach (int gap in gaps)
            {
                messages.Add($"  Folge {gap:D3}");
            }

            return messages;
        }

    }
}
