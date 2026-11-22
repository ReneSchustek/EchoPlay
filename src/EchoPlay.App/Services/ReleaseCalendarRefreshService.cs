using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Core.Http;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Baut im Hintergrund den Veröffentlichungskalender auf: Für jede vorhandene Serie,
    /// lokal wie online, fragt er bei iTunes nach, was ab dem Vormonat erschienen oder
    /// angekündigt ist.
    /// </summary>
    /// <remarks>
    /// Je Serie sind das zwei bis drei Anfragen, und die Ratenbremse lässt höchstens alle
    /// anderthalb Sekunden eine an iTunes durch. Bei hundert Serien dauert ein Aufbau
    /// deshalb einige Minuten. Er läuft darum nach dem Start nebenher, schreibt seine
    /// Ergebnisse in Stapeln und gilt danach einen Tag.
    /// </remarks>
    internal sealed class ReleaseCalendarRefreshService : IDisposable
    {
        /// <summary>
        /// Wie lange ein vollständiger Aufbau gilt. Ankündigungen ändern sich selten
        /// stündlich; ein Tag hält die Liste aktuell, ohne iTunes ständig zu fragen.
        /// </summary>
        private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

        /// <summary>
        /// Wie oft die Schleife nachsieht, ob der Aufbau veraltet ist. Bleibt die Anwendung
        /// über Nacht offen, ist die Liste so spätestens eine Stunde nach Ablauf erneuert.
        /// </summary>
        private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

        /// <summary>
        /// Wartezeit vor dem ersten Aufbau. Bis dahin hat das Startbild die überwachten
        /// Serien geprüft, die dieselbe Ratenbremse benutzen, und die Startseite steht.
        /// </summary>
        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Serien je Schreibvorgang. Bei zehn Serien zeigt die Startseite erste Treffer nach
        /// etwa einer halben Minute statt erst nach dem ganzen Lauf.
        /// </summary>
        internal const int BatchSize = 10;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IClock _clock;
        private readonly TimeZoneInfo _timeZone;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger _logger;
        private CancellationTokenSource? _cts;
        private Task? _runningTask;
        private volatile bool _isRefreshing;
        private volatile bool _stopped;

        /// <summary>
        /// Initialisiert den Dienst.
        /// </summary>
        /// <param name="scopeFactory">Öffnet je Stapel einen eigenen Bereich für Datenbank und Prüfer.</param>
        /// <param name="clock">Zeitquelle für Stichtag und Alter des Aufbaus.</param>
        /// <param name="loggerFactory">Fabrik für den Protokollkanal.</param>
        /// <param name="timeZone">Zeitzone für die Monatsgrenzen; ohne Angabe die des Systems.</param>
        /// <param name="timeProvider">
        /// Taktgeber für die Wartezeiten der Schleife; ohne Angabe die Systemuhr. Tests drehen ihn
        /// vor, statt eine Minute und eine Stunde wirklich abzuwarten.
        /// </param>
        public ReleaseCalendarRefreshService(
            IServiceScopeFactory scopeFactory,
            IClock clock,
            ILoggerFactory loggerFactory,
            TimeZoneInfo? timeZone = null,
            TimeProvider? timeProvider = null)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);

            _scopeFactory = scopeFactory;
            _clock = clock;
            _timeZone = timeZone ?? TimeZoneInfo.Local;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _logger = loggerFactory.CreateLogger("ReleaseCalendarRefreshService");
        }

        /// <summary>
        /// Meldet, dass neue Einträge im Kalender stehen oder ein Aufbau zu Ende ist.
        /// Kommt aus dem Hintergrund; Abonnenten wechseln selbst auf den Oberflächen-Thread.
        /// </summary>
        public event Action? CalendarChanged;

        /// <summary>Ob gerade ein Aufbau läuft.</summary>
        public bool IsRefreshing => _isRefreshing;

        /// <summary>Startet die Schleife. Ein weiterer Aufruf bewirkt nichts.</summary>
        public void Start()
        {
            if (_runningTask is not null)
            {
                return;
            }

            _stopped = false;
            _cts = new CancellationTokenSource();
            // Task.Run löst die Schleife vom Synchronisationskontext des Aufrufers und gibt
            // StopAsync etwas, auf dessen Ende es warten kann.
            _runningTask = Task.Run(() => RunLoopAsync(_cts.Token));
        }

        /// <summary>
        /// Hält die Schleife an und wartet höchstens <paramref name="timeout"/> auf ihr Ende.
        /// </summary>
        /// <param name="timeout">Höchste Wartezeit.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Der Task ist abgeschlossen, wenn die Schleife steht oder die Wartezeit um ist.</returns>
        public async Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            // Ab hier still: Beim Beenden käme die Meldung erst an, wenn die Startseite ihren
            // Container schon verloren hat, und ihr Nachladen liefe ins Leere.
            _stopped = true;
            if (_cts is null || _runningTask is null)
            {
                return;
            }

            await _cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await _runningTask.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Erwartet: Der laufende Stapel hat den Abbruch beachtet.
            }
            catch (TimeoutException)
            {
                _logger.Warning(
                    "Aufbau des Veröffentlichungskalenders nach {TimeoutSeconds}s nicht beendet.",
                    timeout.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture));
            }

            _cts.Dispose();
            _cts = null;
            _runningTask = null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _runningTask = null;
        }

        /// <summary>
        /// Ein Durchgang: Ist der letzte vollständige Aufbau jünger als ein Tag oder steht
        /// die Anwendung im Offline-Modus, geschieht nichts. Sonst werden alle Serien in
        /// Stapeln geprüft.
        /// </summary>
        /// <param name="cancellationToken">Bricht den Durchgang ab; Geschriebenes bleibt.</param>
        /// <returns>Anzahl der geschriebenen Einträge.</returns>
        public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            using EchoPlay.Logger.Scoping.LogScope jobScope = _logger.BeginScope(EchoPlay.App.Logging.JobScopes.ReleaseCalendar);

            DateTime startedAt = _clock.UtcNow;
            DateTime cutoffUtc = ReleaseCalendarMonth.FromUtc(startedAt, _timeZone).Previous.StartUtc(_timeZone);

            IReadOnlyList<CheckableSeriesInfo>? checkable = await PrepareAsync(startedAt, cutoffUtc, cancellationToken).ConfigureAwait(false);
            if (checkable is null)
            {
                return 0;
            }

            _isRefreshing = true;
            int written = 0;
            try
            {
                foreach (CheckableSeriesInfo[] batch in checkable.Chunk(BatchSize))
                {
                    written += await RefreshBatchAsync(batch, cutoffUtc, cancellationToken).ConfigureAwait(false);
                }

                await MarkRefreshedAsync(cancellationToken).ConfigureAwait(false);

                _logger.Info(
                    "Veröffentlichungskalender aufgebaut: {EntryCount} Einträge aus {SeriesCount} Serien.",
                    written, checkable.Count);
                return written;
            }
            finally
            {
                _isRefreshing = false;
                // Auch nach einem Abbruch: Die Startseite nimmt dann ihren Hinweis „wird aufgebaut" zurück.
                RaiseCalendarChanged();
            }
        }

        /// <summary>Meldet eine Änderung, solange der Dienst nicht angehalten ist.</summary>
        private void RaiseCalendarChanged()
        {
            if (!_stopped)
            {
                CalendarChanged?.Invoke();
            }
        }

        /// <summary>Hält fest, dass der Aufbau vollständig durchlief; erst dann gilt er für einen Tag.</summary>
        private async Task MarkRefreshedAsync(CancellationToken cancellationToken)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IReleaseCalendarDataService>()
                .SetLastRefreshAsync(_clock.UtcNow, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Prüft, ob ein Aufbau fällig ist, räumt vergangene Monate ab und liefert die zu
        /// prüfenden Serien.
        /// </summary>
        /// <returns>Die Serien, oder <c>null</c>, wenn kein Aufbau fällig ist.</returns>
        private async Task<IReadOnlyList<CheckableSeriesInfo>?> PrepareAsync(
            DateTime now, DateTime cutoffUtc, CancellationToken cancellationToken)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IServiceProvider services = scope.ServiceProvider;

            AppSettings settings = await services.GetRequiredService<IAppSettingsDataService>()
                .GetAsync(cancellationToken).ConfigureAwait(false);
            if (settings.OfflineMode)
            {
                return null;
            }

            IReleaseCalendarDataService calendar = services.GetRequiredService<IReleaseCalendarDataService>();

            // Vor dem Vormonat zeigt die Ansicht nichts mehr an.
            _ = await calendar.RemoveOlderThanAsync(cutoffUtc, cancellationToken).ConfigureAwait(false);

            DateTime? lastRefresh = await calendar.GetLastRefreshAsync(cancellationToken).ConfigureAwait(false);
            if (lastRefresh is not null && now - lastRefresh.Value < MaxAge)
            {
                _logger.Debug("Veröffentlichungskalender ist aktuell (< 24 h).");
                return null;
            }

            IReadOnlyList<Series> series = await services.GetRequiredService<ISeriesDataService>()
                .GetAllAsync(cancellationToken).ConfigureAwait(false);

            return series.Select(s => new CheckableSeriesInfo
            {
                SeriesId = s.Id,
                Title = s.Title,
                AppleMusicArtistId = s.AppleMusicArtistId,
                LocalFolderPath = s.LocalFolderPath,
                CoverImageUrl = WebAddress.TryParse(s.CoverImageUrl),
                // Der Kalender fragt alle Serien ab, auch solche, die nie bei iTunes gesucht
                // wurden. Ein geratener Künstler brächte fremde Folgen in die Liste und bliebe
                // an der Serie gespeichert.
                RequireExactArtistName = true
            }).ToArray();
        }

        /// <summary>Prüft einen Stapel Serien und schreibt die Treffer.</summary>
        /// <returns>Anzahl der geschriebenen Einträge.</returns>
        private async Task<int> RefreshBatchAsync(
            IReadOnlyList<CheckableSeriesInfo> batch, DateTime cutoffUtc, CancellationToken cancellationToken)
        {
            // Ein Bereich je Stapel: Ein über Minuten offener Kontext sammelte sonst
            // Verfolgungsdaten aller Serien an.
            using IServiceScope scope = _scopeFactory.CreateScope();
            IServiceProvider services = scope.ServiceProvider;

            // Fehler einzelner Serien fängt der Prüfer selbst ab und protokolliert sie.
            IReadOnlyList<OnlineEpisodeCheckResult> results = await services.GetRequiredService<IOnlineEpisodeChecker>()
                .CheckNewReleasesAsync(batch, cutoffUtc, cancellationToken).ConfigureAwait(false);

            DateTime checkedAt = _clock.UtcNow;
            List<ReleaseCalendarEntry> entries = [.. results.SelectMany(result => result.NewReleaseEpisodes.Select(release => new ReleaseCalendarEntry
            {
                SeriesId = result.SeriesId,
                Title = release.Title,
                EpisodeNumber = release.EpisodeNumber,
                ReleaseDate = release.ReleaseDate,
                CoverUrl = release.CoverUrl?.OriginalString,
                CollectionId = release.CollectionId,
                CheckedAtUtc = checkedAt
            }))];

            if (entries.Count == 0)
            {
                return 0;
            }

            await services.GetRequiredService<IReleaseCalendarDataService>()
                .UpsertRangeAsync(entries, cancellationToken).ConfigureAwait(false);

            RaiseCalendarChanged();
            return entries.Count;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Hintergrundschleife: Datenbank- oder Anbieterfehler eines Durchgangs dürfen die Schleife nicht beenden; sie werden protokolliert, und der nächste Durchgang versucht es erneut.")]
        private async Task RunLoopAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(InitialDelay, _timeProvider, ct).ConfigureAwait(false);

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        _ = await RunOnceAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error("Aufbau des Veröffentlichungskalenders fehlgeschlagen.", ex);
                    }

                    await Task.Delay(PollInterval, _timeProvider, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Erwartet beim Beenden der Anwendung.
            }
        }
    }
}
