using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Lädt fehlende Cover automatisch nach: einmal beim App-Start, danach periodisch.
    /// Der Nutzer merkt nichts davon – Cover erscheinen einfach irgendwann.
    /// </summary>
    /// <remarks>
    /// Diese Klasse entscheidet <b>wann</b> etwas passiert, nicht wie. Die Arbeit liegt in drei
    /// Mitspielern, die sie im Konstruktor aufbaut:
    /// <list type="bullet">
    /// <item><see cref="LocalCoverPhases"/> — Dateisystem und SQL-Kopie, ohne Netz.</item>
    /// <item><see cref="OnlineCoverPhases"/> — Anbieter-Adressen, Downloads, Online-Suche.</item>
    /// <item><see cref="ForegroundCoverCoordinator"/> — alles, was am Hintergrundlauf vorbei darf,
    /// weil der Nutzer davor sitzt.</item>
    /// </list>
    /// Die Mitspieler entstehen hier statt über die DI-Registrierung, weil sie keine eigene
    /// Lebensdauer haben und niemand sie einzeln ersetzt — dasselbe Muster wie bei den
    /// Actions-Klassen der ViewModels. Der Logger wird an alle drei weitergegeben, damit die
    /// Meldungen eines Durchlaufs im Protokoll zusammenbleiben.
    /// </remarks>
    public class BackgroundCoverService : IDisposable
    {
        private readonly LocalCoverPhases _localPhases;
        private readonly OnlineCoverPhases _onlinePhases;
        private readonly ForegroundCoverCoordinator _foreground;
        private readonly BackgroundCoverServiceOptions _options;
        private readonly ILogger _logger;
        private CancellationTokenSource? _cts;
        private Task? _backgroundTask;

        /// <summary>
        /// Initialisiert den Background-Cover-Service.
        /// </summary>
        public BackgroundCoverService(
            IServiceScopeFactory scopeFactory,
            ICoverService coverService,
            ICoverDownloader coverDownloader,
            ISpotifyCredentialStore credentialStore,
            BackgroundCoverServiceOptions options,
            ILoggerFactory loggerFactory,
            IClock clock,
            IHostRateLimiter? rateLimiter = null)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _options = options;
            _logger = loggerFactory.CreateLogger("BackgroundCoverService");

            _localPhases = new LocalCoverPhases(scopeFactory, coverService, _logger);
            _onlinePhases = new OnlineCoverPhases(
                scopeFactory, coverService, coverDownloader, credentialStore, clock, rateLimiter, _logger);
            _foreground = new ForegroundCoverCoordinator(
                scopeFactory, coverService, coverDownloader, rateLimiter, _logger);
        }

        /// <summary>
        /// Startet den Hintergrund-Task. Idempotent — mehrfacher Aufruf ist no-op.
        /// </summary>
        public void Start()
        {
            if (_backgroundTask is not null) return;

            _cts = new CancellationTokenSource();
            // Task.Run entkoppelt von einem evtl. vorhandenen UI-SynchronizationContext und
            // macht den Task als Referenz greifbar, damit StopAsync mit Timeout warten kann.
            _backgroundTask = Task.Run(() => RunAsync(_cts.Token));
        }

        /// <summary>
        /// Stoppt den Hintergrund-Task sauber und wartet mit Timeout auf das Ende
        /// der laufenden Iteration. Bei Timeout wird eine Warnung geloggt.
        /// </summary>
        /// <param name="timeout">Maximale Wartezeit.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (_cts is null || _backgroundTask is null) return;

            await _cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await _backgroundTask.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Erwartet: Iteration hat den CancellationToken sauber beobachtet.
            }
            catch (TimeoutException)
            {
                _logger.Warning("BackgroundCoverService: Iteration hat Timeout ({TimeoutSeconds}s) überschritten und wird hart abgebrochen.", timeout.TotalSeconds.ToString("F1", CultureInfo.CurrentCulture));
            }

            _cts.Dispose();
            _cts = null;
            _backgroundTask = null;
        }

        /// <summary>
        /// Ein vollständiger Durchlauf mit einer Protokollzeile je Phase.
        /// Die Reihenfolge ist nach Kosten sortiert: Dateisystem, SQL-Kopie, Anbieter-Adressen,
        /// Download über bekannte Adresse — und erst am Ende die Online-Suche, die als einzige
        /// Phase mehrere Anbieter abfragt.
        /// </summary>
        /// <returns>Anzahl der geladenen Cover.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public virtual async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            // Eigene CTS, die mit dem externen Token verkettet ist — Aufrufer kann den Lauf
            // abbrechen, ohne dass der interne Loop seinen eigenen Schutz verliert.
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationToken ct = cts.Token;

            int loaded = 0;

            // Phase 1a/1b: Lokale Serien- und Episoden-Cover
            int seriesLocalLoaded = await _localPhases.LoadMissingLocalSeriesCoversAsync(ct);
            int episodeLocalLoaded = await _localPhases.LoadMissingLocalEpisodeCoversAsync(ct);
            loaded += seriesLocalLoaded + episodeLocalLoaded;
            _logger.Info(
                "RunOnce Phase 1 (lokal): {TotalLoaded} Cover geladen ({SeriesLoaded} Serien, {EpisodeLoaded} Episoden).",
                seriesLocalLoaded + episodeLocalLoaded, seriesLocalLoaded, episodeLocalLoaded);

            // Phase 1c: Cover von lokalen auf Online-Episoden kopieren (reines SQL)
            int copied = await _localPhases.CopyLocalToOnlineAsync(ct);
            loaded += copied;
            _logger.Info("RunOnce Phase 1b (lokal→online Kopie): {Copied} Cover kopiert.", copied);

            // Phase 2a: CoverImageUrl bei Online-Episoden nachtragen (Provider-API)
            int urlsUpdated = await _onlinePhases.UpdateMissingCoverUrlsAsync(ct);
            _logger.Info("RunOnce Phase 2a (URL-Nachtrag): {UrlsUpdated} URLs gesetzt.", urlsUpdated);

            // Phase 2b: Fehlende Cover über Provider-URLs herunterladen (Serien + Episoden)
            int seriesProviderLoaded = await _onlinePhases.DownloadMissingSeriesProviderCoversAsync(ct);
            int episodeProviderLoaded = await _onlinePhases.DownloadMissingEpisodeProviderCoversAsync(ct);
            loaded += seriesProviderLoaded + episodeProviderLoaded;
            _logger.Info(
                "RunOnce Phase 2b (Provider-URL Download): {TotalLoaded} Cover geladen ({SeriesLoaded} Serien, {EpisodeLoaded} Episoden).",
                seriesProviderLoaded + episodeProviderLoaded, seriesProviderLoaded, episodeProviderLoaded);

            // Phase 3: Was weder lokal noch über eine Provider-URL erreichbar war, bleibt ohne
            // die Online-Suche für immer ohne Cover.
            int searched = await _onlinePhases.SearchMissingEpisodeCoversOnlineAsync(ct);
            loaded += searched;
            _logger.Info("RunOnce Phase 3 (Online-Suche): {Searched} Serien mit fehlenden Episoden-Covern angestoßen.", searched);

            // Phase 4: Dasselbe für die Serien selbst. Ohne diesen Schritt bleibt eine Serie
            // ohne lokale cover.jpg und ohne Provider-URL dauerhaft leer — der URL-Nachtrag
            // in Phase 2a füllt ausschließlich Episoden.
            int seriesFound = await _onlinePhases.SearchMissingSeriesCoversOnlineAsync(ct);
            loaded += seriesFound;
            _logger.Info("RunOnce Phase 4 (Serien-Online-Suche): {Found} Serien-Cover gefunden.", seriesFound);

            return loaded;
        }

        /// <summary>
        /// Splash-Pfad: lädt ausschließlich fehlende Serien-Cover (lokal + optional Provider-URL).
        /// Kein Episoden-Scan, kein ID3-Tag-Parsing, kein Provider-Call für Folgen.
        /// Provider-URL-Download wird übersprungen, wenn <paramref name="isOnlineAvailable"/>
        /// <see langword="false"/> ist (Offline-Modus oder fehlgeschlagener Konnektivitäts-Check).
        /// </summary>
        /// <param name="isOnlineAvailable">Steuert, ob der Provider-URL-Download laufen darf.</param>
        /// <param name="ct">Cancellation-Token des Splash-Pfades.</param>
        /// <returns>Anzahl der geladenen Serien-Cover.</returns>
        public virtual async Task<int> RunSeriesCoversOnceAsync(bool isOnlineAvailable, CancellationToken ct = default)
        {
            int loaded = await _localPhases.LoadMissingLocalSeriesCoversAsync(ct);
            _logger.Info("SplashCoverPhase Serien lokal: {LocalLoaded} Cover geladen.", loaded);

            if (isOnlineAvailable)
            {
                int providerLoaded = await _onlinePhases.DownloadMissingSeriesProviderCoversAsync(ct);
                loaded += providerLoaded;
                _logger.Info("SplashCoverPhase Serien Provider: {ProviderLoaded} Cover geladen.", providerLoaded);
            }
            else
            {
                _logger.Info("SplashCoverPhase Serien Provider: übersprungen (offline).");
            }

            return loaded;
        }

        /// <summary>
        /// Hauptschleife: einmaliger Durchlauf beim Start, dann periodisch. Protokolliert je
        /// Iteration nur eine Zusammenfassung und schweigt ganz, wenn nichts gefunden wurde —
        /// alle 30 Minuten eine Zeile je Phase wäre Protokoll-Müll.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Hintergrund-Scan-Schleife: TagLib-, DB-, HTTP- oder IO-Fehler einer einzelnen Iteration dürfen die Cover-Schleife nicht beenden; Fehler werden als Warning geloggt und die nächste Iteration fährt fort.")]
        private async Task RunAsync(CancellationToken ct)
        {
            // Kurz warten, damit die App vollständig initialisiert ist
            await Task.Delay(_options.InitialDelay, ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await _foreground.WaitWhileInFlightAsync(ct).ConfigureAwait(false);

                    int seriesLocalLoaded = await _localPhases.LoadMissingLocalSeriesCoversAsync(ct);
                    int episodeLocalLoaded = await _localPhases.LoadMissingLocalEpisodeCoversAsync(ct);
                    int copied = await _localPhases.CopyLocalToOnlineAsync(ct);

                    // Der URL-Nachtrag zählt nicht mit: eine nachgetragene Adresse ist noch kein
                    // Cover, sie ist die Voraussetzung für den Download zwei Zeilen weiter.
                    _ = await _onlinePhases.UpdateMissingCoverUrlsAsync(ct);

                    int seriesProviderLoaded = await _onlinePhases.DownloadMissingSeriesProviderCoversAsync(ct);
                    int episodeProviderLoaded = await _onlinePhases.DownloadMissingEpisodeProviderCoversAsync(ct);
                    _ = await _onlinePhases.SearchMissingEpisodeCoversOnlineAsync(ct);
                    int seriesSearchFound = await _onlinePhases.SearchMissingSeriesCoversOnlineAsync(ct);

                    int localLoaded = seriesLocalLoaded + episodeLocalLoaded;
                    int providerLoaded = seriesProviderLoaded + episodeProviderLoaded;
                    int total = localLoaded + copied + providerLoaded + seriesSearchFound;

                    if (total > 0)
                    {
                        _logger.Info(
                            "Hintergrund: {LocalLoaded} lokal, {Copied} kopiert, {ProviderLoaded} Provider, {SeriesSearchFound} Serien-Suche.",
                            localLoaded, copied, providerLoaded, seriesSearchFound);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Warning("Hintergrund-Cover-Scan fehlgeschlagen: {Reason}", ex.Message);
                }

                try
                {
                    await Task.Delay(_options.Interval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Lädt die Cover für die angegebenen Episoden (sofern fehlend) priorisiert nach.
        /// Wird vom Dashboard nach dem ersten Rendern aufgerufen, damit Kacheln mit
        /// Serien-Cover-Fallback das spezifische Folgen-Cover progressiv nachbekommen.
        /// Details siehe <see cref="ForegroundCoverCoordinator"/>.
        /// </summary>
        /// <param name="episodeIds">Zu prüfende Episoden – Duplikate sind erlaubt, werden entfernt.</param>
        /// <param name="onCoverReady">Callback pro Episode, die ein Cover bekommen hat. Darf <see langword="null"/> sein.</param>
        /// <param name="priority">Priorität der Anfrage.</param>
        public void EnqueueForEpisodes(
            IReadOnlyList<Guid> episodeIds,
            Action<Guid, byte[]>? onCoverReady,
            CoverFetchPriority priority = CoverFetchPriority.Background)
            => _foreground.EnqueueForEpisodes(episodeIds, onCoverReady, priority);

        /// <summary>
        /// Priorisiert das Laden der Folgen-Cover für die angegebene Serie, sodass der
        /// Hintergrundlauf zwischen zwei Phasen pausiert. Details siehe
        /// <see cref="ForegroundCoverCoordinator"/>.
        /// </summary>
        /// <param name="seriesId">Serie, deren Folgen-Cover priorisiert geladen werden.</param>
        /// <param name="ct">Abbruch-Token der aufrufenden Detail-Ansicht.</param>
        public virtual Task RequestPriorityForSeriesAsync(Guid seriesId, CancellationToken ct = default)
            => _foreground.RequestPriorityForSeriesAsync(seriesId, ct);

        /// <summary>
        /// Lädt das Cover für ein Such-Treffer-Element. Persistiert es bewusst nicht — ein
        /// Such-Treffer ist noch nicht importiert. Details siehe
        /// <see cref="ForegroundCoverCoordinator"/>.
        /// </summary>
        /// <param name="source">Provider-Schlüssel. Andere Werte verhindern den DB-Lookup.</param>
        /// <param name="sourceSeriesId">Provider-spezifische Serien-ID.</param>
        /// <param name="coverUrl">Cover-URL aus dem Such-Treffer.</param>
        /// <param name="ct">Abbruch-Token der laufenden Suche.</param>
        /// <returns>Cover-Bytes oder <see langword="null"/> bei Fehler/Abbruch ohne Daten.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
            Justification = "Cover-URL stammt aus DTO der externen Provider-API und wird in der gesamten Cover-Pipeline als string verwaltet (gleiches Muster wie ICoverDownloader).")]
        public virtual Task<byte[]?> RequestCoverForSearchResultAsync(
            string source, string sourceSeriesId, string coverUrl, CancellationToken ct = default)
            => _foreground.RequestCoverForSearchResultAsync(source, sourceSeriesId, coverUrl, ct);

        /// <summary>
        /// Gibt an, ob aktuell eine Foreground-Priority-Anfrage verarbeitet wird.
        /// Für Tests und Telemetry.
        /// </summary>
        public bool IsPriorityActive => _foreground.IsActive;

        /// <summary>
        /// Stellt sicher, dass alle lokalen Episoden einer Serie (nach Titel) ihre Cover
        /// in CoverImages haben. Wird synchron vor der Anzeige aufgerufen, damit der
        /// CoverCopyService danach Quellen findet.
        /// </summary>
        /// <param name="seriesTitle">Titel der Serie (z.B. "Fünf Freunde").</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Anzahl der neu geladenen Cover.</returns>
        public Task<int> EnsureLocalCoversForSeriesAsync(string seriesTitle, CancellationToken cancellationToken = default)
            => _localPhases.EnsureLocalCoversForSeriesAsync(seriesTitle, cancellationToken);

        /// <summary>
        /// Kopiert vorhandene Cover von lokalen Episoden auf Online-Episoden derselben Serie.
        /// Reines SQL, kein Netzwerk.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Anzahl der kopierten Cover.</returns>
        public Task<int> CopyLocalToOnlineAsync(CancellationToken cancellationToken = default)
            => _localPhases.CopyLocalToOnlineAsync(cancellationToken);

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Gibt die CancellationTokenSource und den laufenden Hintergrund-Task frei.
        /// Wartet kurz auf das Ende der aktuellen Iteration, damit kein Service-Scope
        /// als Closure im Task-State-Machine hängen bleibt. Abgeleitete Typen können
        /// überschreiben, dürfen aber den Cleanup-Pfad der Basis
        /// (<c>base.Dispose(disposing)</c>) nicht auslassen.
        /// </summary>
        /// <param name="disposing"><see langword="true"/> bei deterministischem Dispose; <see langword="false"/> beim Finalizer.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Dispose-Pfad: AggregateException/ObjectDisposedException aus dem abgebrochenen Hintergrund-Task dürfen den Shutdown nicht zerlegen, weil der Service als Singleton meist im App-Exit disposed wird.")]
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            try { _cts?.Cancel(); }
            catch (ObjectDisposedException)
            {
                // Race im App-Exit: CTS wurde parallel bereits disposed – Cancel ist dann ein No-Op.
            }

            // 2 s sind ein Kompromiss: Task.Delay-Iterationen schlafen 30 min,
            // ein laufendes RunOnceAsync braucht selten so lange — länger warten würde
            // den App-Exit blockieren.
            try { _ = _backgroundTask?.Wait(TimeSpan.FromSeconds(2)); }
            catch (Exception)
            {
                // AggregateException (Cancel) oder Timeout im Shutdown sind erwartet – Dispose darf hier nicht werfen.
            }

            _cts?.Dispose();
            _cts = null;
            _backgroundTask = null;
        }
    }
}
