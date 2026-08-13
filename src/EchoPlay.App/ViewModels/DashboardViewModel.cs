using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// ViewModel für die Startseite.
    /// </summary>
    /// <remarks>
    /// Die Seite besteht aus fünf Abschnitten — Neuerscheinungen, Favoriten, Weiterhören,
    /// „läuft gerade" und „zuletzt gehört" —, und jeder hat sein eigenes Ansichtsmodell.
    /// Dieses hier liest die gemeinsame Datengrundlage einmal
    /// (<see cref="DashboardSnapshot"/>), lässt die Abschnitte daraus bauen und verteilt die
    /// Ergebnisse. Bindungen sprechen die Abschnitte direkt an, etwa
    /// <c>ViewModel.NeuerscheinungenVM.NewEpisodeGroups</c>.
    /// </remarks>
    public sealed class DashboardViewModel : ObservableObject, IDisposable
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;
        private readonly DashboardCoverProvider _coverProvider;
        private readonly DashboardDataLoader _dataLoader;
        private readonly DashboardSeriesSectionBuilder _sectionBuilder;
        private readonly NewReleaseGroupBuilder _newReleaseBuilder;
        private readonly INewReleaseEventService? _newReleaseEventService;
        private readonly DispatcherQueue? _uiDispatcherQueue;

        private bool _isLoading;

        // Jeder Ladelauf bricht den vorigen ab und legt ein neues Abbruchzeichen an. Beim
        // Freigeben wird das laufende gestoppt.
        private CancellationTokenSource? _loadCts;

        /// <summary>
        /// Initialisiert das ViewModel mit allen benötigten Services.
        /// </summary>
        /// <param name="context">Bündelt alle per DI aufgelösten Dienste.</param>
        internal DashboardViewModel(DashboardViewModelContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(context.LoggerFactory);

            _scopeFactory = context.ScopeFactory;
            _logger = context.LoggerFactory.CreateLogger("DashboardViewModel");

            IClock resolvedClock = context.Clock ?? new SystemClock();
            _uiDispatcherQueue = TryGetDispatcherQueue();

            _coverProvider = new DashboardCoverProvider(
                context.ScopeFactory, context.CoverService, context.BackgroundCoverService, _uiDispatcherQueue);

            NewEpisodeCardServices cardServices = new(
                context.ErrorDialogService,
                context.ConfirmationDialogService,
                context.PlayerService,
                context.LocalizationService);

            _dataLoader = new DashboardDataLoader(context.ScopeFactory, _coverProvider, cardServices, resolvedClock);
            _sectionBuilder = new DashboardSeriesSectionBuilder(
                context.ScopeFactory, _coverProvider, context.ConfirmationDialogService, context.LocalizationService);
            _newReleaseBuilder = new NewReleaseGroupBuilder(
                context.ScopeFactory, _coverProvider, cardServices, resolvedClock, _logger);

            NeuerscheinungenVM = new DashboardNeuerscheinungenViewModel();
            FavoritenVM = new DashboardFavoritenViewModel(context.ScopeFactory, _logger);
            WeiterhoerenVM = new DashboardWeiterhoerenViewModel();
            InProgressVM = new DashboardInProgressViewModel();
            ZuletztGehoertVM = new DashboardRecentlyPlayedViewModel();

            // Entfernt oder sortiert der Nutzer Favoriten, ändert sich der Hinweis darüber.
            FavoritenVM.FavoritesChanged += OnFavoritesChanged;

            // Die Prüfung auf Neuerscheinungen läuft nach dem Favorisieren im Hintergrund
            // weiter — ohne dieses Abo bliebe die schon gezeichnete Seite auf altem Stand.
            _newReleaseEventService = context.NewReleaseEventService;
            if (_newReleaseEventService is not null)
            {
                _newReleaseEventService.CacheChanged += OnNewReleaseCacheChanged;
            }
        }

        /// <summary>Sub-VM für den Neuerscheinungen-Abschnitt.</summary>
        public DashboardNeuerscheinungenViewModel NeuerscheinungenVM { get; }

        /// <summary>Sub-VM für den Favoriten-Abschnitt.</summary>
        public DashboardFavoritenViewModel FavoritenVM { get; }

        /// <summary>Sub-VM für den „Weiterhören"-Abschnitt.</summary>
        public DashboardWeiterhoerenViewModel WeiterhoerenVM { get; }

        /// <summary>Sub-VM für den „läuft gerade"-Abschnitt.</summary>
        public DashboardInProgressViewModel InProgressVM { get; }

        /// <summary>Sub-VM für den „Zuletzt gehört"-Abschnitt.</summary>
        public DashboardRecentlyPlayedViewModel ZuletztGehoertVM { get; }

        /// <summary>
        /// Die Hinweise, mit denen die Seite einen noch leeren Bestand erklärt.
        /// </summary>
        public DashboardOnboardingHints Hints { get; } = new();

        /// <summary>
        /// Gibt an, ob gerade ein Ladevorgang läuft. Steuert den ProgressRing auf der Startseite.
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        /// <inheritdoc cref="DashboardFavoritenViewModel.SaveFavoriteSeriesOrderAsync"/>
        public Task SaveFavoriteSeriesOrderAsync() => FavoritenVM.SaveFavoriteSeriesOrderAsync();

        /// <summary>
        /// Lädt alle Abschnitte der Startseite und verteilt die Ergebnisse an die Sub-VMs.
        /// Neuerscheinungen werden im Offline-Modus übersprungen.
        /// </summary>
        /// <returns>Der Task ist abgeschlossen, wenn alle Abschnitte gefüllt und an die Sub-ViewModels verteilt sind.</returns>
        public async Task LoadAsync()
        {
            CancellationToken ct = await BeginLoadSessionAsync();

            // Zeitmessung für die Diagnose: alle Bau-Schritte und die HTTP-/DB-Zeilen darunter
            // erscheinen im Protokoll unter derselben Kennung.
            using IDisposable ua = UserActionScope.BeginUserAction("DashboardLoad");
            Stopwatch totalStopwatch = Stopwatch.StartNew();

            IsLoading = true;
            DashboardSnapshot snapshot;

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IServiceProvider services = scope.ServiceProvider;

                IEpisodeDataService episodeService = services.GetRequiredService<IEpisodeDataService>();
                IDashboardPositionDataService positionService = services.GetRequiredService<IDashboardPositionDataService>();

                snapshot = await DashboardSnapshot.LoadAsync(
                    services.GetRequiredService<ISeriesDataService>(),
                    services.GetRequiredService<IPlaybackStateDataService>(),
                    services.GetRequiredService<IAppSettingsDataService>(),
                    positionService,
                    ReadStartupResult(),
                    _logger);

                Hints.Apply(snapshot);

                // Alle sichtbar werdenden Folgen-Cover in einer Abfrage, bevor die Abschnitte
                // bauen — sonst löst jede Kachel ihre eigene aus.
                await _coverProvider.BeginSessionAsync(snapshot.RelevantEpisodeIds, ct);

                await LoadSectionsAsync(episodeService, positionService, snapshot, ct);
            }
            finally
            {
                IsLoading = false;
            }

            // Neuerscheinungen entfallen im Offline-Modus vollständig. Die zwischengespeicherten
            // Daten bleiben erhalten, sie werden nur nicht gezeigt.
            if (!snapshot.OfflineMode)
            {
                Stopwatch newReleaseStopwatch = Stopwatch.StartNew();
                NeuerscheinungenVM.SetGroups(await _newReleaseBuilder.BuildAsync(snapshot.SubscribedSeries, ct));
                _logger.Debug(() => $"BuildNewReleases dauer={newReleaseStopwatch.ElapsedMilliseconds} ms");
            }

            // Kacheln, die nur das Serien-Cover zeigen, bekommen ihr Folgen-Cover jetzt
            // nachgetragen — nach dem Zeichnen, nicht davor.
            _coverProvider.FlushPendingRefresh();

            _logger.Info("DashboardLoad total={ElapsedMs} ms", totalStopwatch.ElapsedMilliseconds);
        }

        /// <summary>
        /// Löst alle Event-Subscriptions und gibt das Favoriten-Sub-VM frei.
        /// </summary>
        public void Dispose()
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;

            FavoritenVM.FavoritesChanged -= OnFavoritesChanged;

            if (_newReleaseEventService is not null)
            {
                _newReleaseEventService.CacheChanged -= OnNewReleaseCacheChanged;
            }

            FavoritenVM.Dispose();
        }

        /// <summary>
        /// Bricht einen noch laufenden Ladelauf ab und beginnt einen neuen. Verlässt der
        /// Nutzer die Seite oder kehrt er zurück, sollen offene Aufrufe keinen verworfenen
        /// Zustand mehr beschreiben.
        /// </summary>
        /// <returns>Das Abbruchzeichen des neuen Laufs.</returns>
        private async Task<CancellationToken> BeginLoadSessionAsync()
        {
            CancellationTokenSource? previous = _loadCts;
            _loadCts = new CancellationTokenSource();
            CancellationToken ct = _loadCts.Token;

            if (previous is not null)
            {
                await previous.CancelAsync();
                previous.Dispose();
            }

            return ct;
        }

        /// <summary>
        /// Baut die vier Abschnitte, die aus den Wiedergabeständen kommen, und verteilt sie.
        /// Jeder Schritt wird einzeln gemessen — so ist im Protokoll ablesbar, welcher
        /// Abschnitt einen langsamen Start verursacht.
        /// </summary>
        private async Task LoadSectionsAsync(
            IEpisodeDataService episodeService,
            IDashboardPositionDataService positionService,
            DashboardSnapshot snapshot,
            CancellationToken ct)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            WeiterhoerenVM.SetItems(await _sectionBuilder.BuildUnheardSeriesAsync(
                snapshot.FavoriteSeries, episodeService, snapshot.StateByEpisodeId));
            _logger.Debug(() => $"BuildUnheard dauer={stopwatch.ElapsedMilliseconds} ms");
            stopwatch.Restart();

            FavoritenVM.SetItems(await _sectionBuilder.BuildFavoriteCardsAsync(snapshot.FavoriteSeries, positionService));
            _logger.Debug(() => $"BuildFavorites dauer={stopwatch.ElapsedMilliseconds} ms");
            stopwatch.Restart();

            InProgressVM.SetItems(await _dataLoader.BuildInProgressEpisodesAsync(
                episodeService, snapshot.AllStates, snapshot.SubscribedSeries, ct));
            _logger.Debug(() => $"BuildInProgress dauer={stopwatch.ElapsedMilliseconds} ms");
            stopwatch.Restart();

            ZuletztGehoertVM.SetItems(await _dataLoader.BuildRecentSeriesAsync(
                episodeService, snapshot.AllStates, snapshot.SubscribedSeries, ct));
            _logger.Debug(() => $"BuildRecent dauer={stopwatch.ElapsedMilliseconds} ms");
        }

        /// <summary>
        /// Liest das Ergebnis des Startlaufs. Es enthält den Offline-Zustand; ist der
        /// Startlauf noch nicht durch, liefert die Methode nichts und die Einstellung wird
        /// stattdessen gelesen.
        /// </summary>
        private static StartupResult? ReadStartupResult()
        {
            try
            {
                return App.StartupResultData;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>
        /// Fängt die Warteschlange der Oberfläche ein, damit der Hintergrund-Rückruf für
        /// Cover Bilder auf dem richtigen Thread erzeugen kann. In Tests ohne laufende
        /// Oberfläche bleibt sie leer.
        /// </summary>
        private static DispatcherQueue? TryGetDispatcherQueue()
        {
            try
            {
                return DispatcherQueue.GetForCurrentThread();
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                return null;
            }
        }

        /// <summary>
        /// Frischt den Favoriten-Hinweis auf, wenn der Nutzer Favoriten entfernt oder umsortiert.
        /// </summary>
        private void OnFavoritesChanged()
            => Hints.UpdateFavorites(FavoritenVM.FavoriteSeries.Count > 0);

        /// <summary>
        /// Lädt die Startseite neu, nachdem sich der Neuerscheinungen-Zwischenspeicher
        /// geändert hat. Der Auslöser läuft im Hintergrund, deshalb der Wechsel auf den
        /// Oberflächen-Thread. Ohne Warteschlange (Tests) wird direkt geladen.
        /// </summary>
        private void OnNewReleaseCacheChanged()
        {
            if (_uiDispatcherQueue is null)
            {
                _ = ReloadSafelyAsync();
                return;
            }

            _ = _uiDispatcherQueue.TryEnqueue(() => _ = ReloadSafelyAsync());
        }

        /// <summary>
        /// Nachladen ohne Aufrufer: Fehler dürfen weder die Seite reißen noch als unbeobachtete
        /// Task-Exception erst im Finalizer auftauchen.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Hintergrund-Nachladen der Startseite: DB- oder Provider-Fehler dürfen die bereits gerenderte Seite nicht beenden; der nächste Besuch lädt ohnehin neu.")]
        private async Task ReloadSafelyAsync()
        {
            try
            {
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _logger.Warning("Nachladen der Startseite fehlgeschlagen: {Reason}", ex.Message);
            }
        }
    }
}
