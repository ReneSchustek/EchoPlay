using EchoPlay.App.Helpers;
using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Scanning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für die Scan- und Status-Logik der lokalen Mediathek.
    /// Kapselt den Ablauf von Scan und Neu-Initialisierung, verwaltet den Einrichtungszustand
    /// der Bibliothek und bedient den Ordnerpicker zum Auswählen oder Hinzufügen von Ordnern.
    /// Die eigentliche Anzeige der Serien übernimmt weiterhin <see cref="MediathekLokalViewModel"/>;
    /// diese Klasse meldet Fortschritt und Abschluss über <see cref="LibraryReloaded"/> und den
    /// Konstruktor-Callback für einzelne synchronisierte Serien zurück.
    /// </summary>
    public sealed class LocalLibraryScanViewModel : ObservableObject, IDisposable
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISyncService _syncService;
        private readonly IErrorDialogService _errorDialogService;
        private readonly IConfirmationDialogService _confirmationDialogService;
        private readonly StatusBarViewModel _statusBar;
        private readonly IScanEventService _scanEventService;
        private readonly Action<Series> _onSeriesSynced;

        /// <summary>
        /// DispatcherQueue des UI-Threads – wird im Konstruktor auf dem UI-Thread erfasst und
        /// später genutzt, um Hintergrundthread-Callbacks des <see cref="IScanEventService"/>
        /// auf den UI-Thread zu marshallen. In Unit-Tests ohne WinUI-3-Dispatcher bleibt das Feld <see langword="null"/>.
        /// </summary>
        private readonly DispatcherQueue? _dispatcherQueue;

        private string _libraryRootPath = string.Empty;
        private bool _isScanning;
        private bool _needsLibraryFolderSetup;
        private string _syncStatusText = string.Empty;
        private bool _isScanIndeterminate = true;
        private double _scanProgressPercent;
        private string _scanDetailText = string.Empty;

        /// <summary>
        /// Initialisiert das Sub-ViewModel mit den benötigten Diensten und einem Callback,
        /// der pro synchronisierter Serie vom übergeordneten ViewModel verarbeitet wird.
        /// </summary>
        /// <param name="scopeFactory">Für Datenbankzugriffe innerhalb von Scan und Reset.</param>
        /// <param name="syncService">Für den Bibliotheks-Scan.</param>
        /// <param name="errorDialogService">Für Fehler-Dialoge.</param>
        /// <param name="confirmationDialogService">Für Bestätigungs-Dialoge bei der Neu-Initialisierung.</param>
        /// <param name="statusBar">Statusleiste für den globalen Scan-Fortschritt.</param>
        /// <param name="scanEventService">Singleton-Dienst für navigationsübergreifende Serie-Sync-Events.</param>
        /// <param name="onSeriesSynced">
        /// Callback des übergeordneten ViewModels, der pro frisch synchronisierter Serie aufgerufen wird.
        /// Der Aufruf erfolgt auf dem UI-Thread, sofern ein <see cref="DispatcherQueue"/> verfügbar ist.
        /// </param>
        public LocalLibraryScanViewModel(
            IServiceScopeFactory scopeFactory,
            ISyncService syncService,
            IErrorDialogService errorDialogService,
            IConfirmationDialogService confirmationDialogService,
            StatusBarViewModel statusBar,
            IScanEventService scanEventService,
            Action<Series> onSeriesSynced)
        {
            _scopeFactory = scopeFactory;
            _syncService = syncService;
            _errorDialogService = errorDialogService;
            _confirmationDialogService = confirmationDialogService;
            _statusBar = statusBar;
            _scanEventService = scanEventService;
            _onSeriesSynced = onSeriesSynced;

            // DispatcherQueue auf dem UI-Thread erfassen – Events des Scan-Dienstes treffen später
            // vom Hintergrundthread ein und müssen auf den UI-Thread gemarshallt werden.
            // In Unit-Tests existiert kein WinUI-3-Dispatcher – dort bleibt _dispatcherQueue null.
            try
            {
                _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                _dispatcherQueue = null;
            }

            ScanCommand = new RelayCommand(() => _ = ScanAsync());
            ReInitializeCommand = new RelayCommand(() => _ = ReInitializeAsync());
            AddFolderCommand = new RelayCommand(() => AddFolderRequested?.Invoke());
        }

        // ── Bibliothek ───────────────────────────────────────────────────────────

        /// <summary>Pfad zur lokalen Hörspielbibliothek laut AppSettings.</summary>
        public string LibraryRootPath
        {
            get => _libraryRootPath;
            set => SetProperty(ref _libraryRootPath, value);
        }

        /// <summary>
        /// Gibt an, ob noch kein Bibliotheksordner konfiguriert wurde.
        /// Steuert die Sichtbarkeit des Einrichtungshinweises.
        /// </summary>
        public bool NeedsLibraryFolderSetup
        {
            get => _needsLibraryFolderSetup;
            set => SetProperty(ref _needsLibraryFolderSetup, value);
        }

        /// <summary>Gibt an, ob gerade ein Scan-Vorgang läuft.</summary>
        public bool IsScanning
        {
            get => _isScanning;
            private set
            {
                if (SetProperty(ref _isScanning, value))
                {
                    OnPropertyChanged(nameof(IsNotScanning));
                    OnPropertyChanged(nameof(IsScanningVisibility));
                }
            }
        }

        /// <summary>
        /// Gibt an, ob kein Scan läuft.
        /// Wird für <c>IsEnabled</c>-Bindungen der Scan-Schaltflächen verwendet,
        /// da WinUI 3 keine Negations-Konverter unterstützt.
        /// </summary>
        public bool IsNotScanning => !_isScanning;

        /// <summary>
        /// Sichtbarkeit des Scan-Overlays – eingeblendet während ein Scan oder eine Neu-Initialisierung läuft.
        /// </summary>
        public Visibility IsScanningVisibility => _isScanning ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Gibt an, ob der Fortschrittsbalken im Scan-Overlay indeterministisch angezeigt werden soll.
        /// </summary>
        public bool IsScanIndeterminate
        {
            get => _isScanIndeterminate;
            private set => SetProperty(ref _isScanIndeterminate, value);
        }

        /// <summary>
        /// Fortschritt in Prozent (0–100) für den deterministischen Fortschrittsbalken im Overlay.
        /// </summary>
        public double ScanProgressPercent
        {
            get => _scanProgressPercent;
            private set => SetProperty(ref _scanProgressPercent, value);
        }

        /// <summary>
        /// Detailtext für das Scan-Overlay, z.B. "Datei 12 von 150".
        /// </summary>
        public string ScanDetailText
        {
            get => _scanDetailText;
            private set
            {
                if (SetProperty(ref _scanDetailText, value))
                {
                    OnPropertyChanged(nameof(ScanDetailVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit des Detail-Texts im Scan-Overlay.
        /// </summary>
        public Visibility ScanDetailVisibility =>
            !string.IsNullOrEmpty(_scanDetailText) ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Statusmeldung des laufenden oder abgeschlossenen Scans.</summary>
        public string SyncStatusText
        {
            get => _syncStatusText;
            private set => SetProperty(ref _syncStatusText, value);
        }

        // ── Befehle ──────────────────────────────────────────────────────────────

        /// <summary>Startet einen neuen Scan der lokalen Bibliothek.</summary>
        public ICommand ScanCommand { get; }

        /// <summary>Setzt alle lokalen Zuordnungen zurück und scannt neu.</summary>
        public ICommand ReInitializeCommand { get; }

        /// <summary>
        /// Fordert das Hinzufügen eines Ordners an – die Page liefert über
        /// <see cref="AddFolderRequested"/> das HWND des Hauptfensters nach.
        /// </summary>
        public ICommand AddFolderCommand { get; }

        // ── Events ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Wird ausgelöst, wenn der Nutzer den Ordner-Hinzufügen-Button drückt.
        /// Die Page reagiert auf das Event und ruft <see cref="AddFolderAsync"/> mit dem Fenster-Handle auf.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "VM->Page-Signal ohne Nutzdaten: die Page ergänzt das HWND erst im Handler; ein EventArgs-Wrapper brachte keinen Mehrwert, da MVVM-strict keine HWND-Referenz ins VM lassen will.")]
        public event Action? AddFolderRequested;

        /// <summary>
        /// Wird ausgelöst, sobald der Bibliotheksinhalt aus der Datenbank neu geladen werden muss –
        /// z.B. nach Abschluss eines Scans, einer Neu-Initialisierung oder einem Ordnerpicker-Ergebnis.
        /// Das übergeordnete ViewModel reagiert darauf und füllt Kacheln, Episoden und Tracks neu.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Async Parent/Sub-VM-Bridge: der Parent-VM-Handler ist asynchron (Re-Load der Kacheln/Episoden/Tracks); Func<Task> erlaubt natives 'await' auf den Subscriber, was EventHandler<T> nicht leistet.")]
        public event Func<Task>? LibraryReloaded;

        /// <summary>
        /// Wird unmittelbar vor Beginn eines Scans oder einer Neu-Initialisierung ausgelöst.
        /// Das übergeordnete ViewModel leert daraufhin seine Listen, damit der Nutzer während
        /// des Scans nicht auf veraltete Kacheln blickt.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Parent-VM-Bridge ohne Nutzdaten: leert Listen vor Scan-Beginn; Action ist semantisch klar, EventArgs-Wrapper wäre ohne Inhalt überflüssig.")]
        public event Action? ScanStarting;

        // ── Aktivierung ──────────────────────────────────────────────────────────

        /// <summary>
        /// Abonniert den <see cref="IScanEventService.SeriesSynced"/>-Stream, damit laufende Scans
        /// auch nach einer Rücknavigation weiterhin live Kacheln einfügen.
        /// </summary>
        public void Activate()
        {
            _scanEventService.SeriesSynced += OnSeriesSyncedInternal;
        }

        /// <summary>
        /// Deabonniert den Event-Stream und verhindert Memory-Leaks durch den Singleton-Service.
        /// </summary>
        public void Deactivate()
        {
            _scanEventService.SeriesSynced -= OnSeriesSyncedInternal;
        }

        /// <summary>
        /// Handler für <see cref="IScanEventService.SeriesSynced"/>.
        /// Wird vom Hintergrundthread des Sync-Dienstes aufgerufen und marshallt den Callback
        /// über den <see cref="DispatcherQueue"/> auf den UI-Thread, bevor das übergeordnete
        /// ViewModel benachrichtigt wird. Die Callback-Implementierung erzeugt WinRT-Objekte
        /// (BitmapImage, InMemoryRandomAccessStream), die zwingend auf dem UI-Thread entstehen müssen.
        /// </summary>
        /// <param name="series">Die synchronisierte Serie.</param>
        private void OnSeriesSyncedInternal(Series series)
        {
            if (_dispatcherQueue is not null)
            {
                _ = _dispatcherQueue.TryEnqueue(() => _onSeriesSynced(series));
            }
            else
            {
                // In Unit-Tests: direkt aufrufen (kein Thread-Wechsel nötig)
                _onSeriesSynced(series);
            }
        }

        // ── Scan / Neu-Initialisierung ───────────────────────────────────────────

        /// <summary>
        /// Startet einen Bibliotheks-Scan über den SyncService.
        /// Serien werden über <see cref="IScanEventService.SeriesSynced"/> sofort sichtbar gemacht;
        /// nach Abschluss meldet das Sub-VM über <see cref="LibraryReloaded"/>, damit das
        /// übergeordnete ViewModel Episodenzähler und Cover konsistent neu lädt.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Bibliotheks-Scan vom Command: IO-/DB-/TagLib-Fehler der vielen gescannten Ordner werden geloggt und der Nutzer sieht die Fehlermeldung in 'StatusMessage', damit ein einzelner defekter Ordner den Scan-Command nicht reißt.")]
        private async Task ScanAsync()
        {
            if (IsScanning)
            {
                return;
            }

            // Listen des übergeordneten ViewModels leeren – veraltete Kacheln dürfen nicht stehen bleiben.
            ScanStarting?.Invoke();

            IsScanning = true;
            IsScanIndeterminate = true;
            ScanProgressPercent = 0;
            ScanDetailText = string.Empty;
            SyncStatusText = SafeResourceLoader.Get("ScanStatusPreparing");

            try
            {
                // Leere DB (z.B. nach "Bibliothek zurücksetzen") zwingt den Scan in den
                // Vollimport-Modus, unabhängig von AutoImportAfterScan in den Einstellungen.
                bool forceImportAll;
                using (IServiceScope checkScope = _scopeFactory.CreateScope())
                {
                    ISeriesDataService checkService = checkScope.ServiceProvider.GetRequiredService<ISeriesDataService>();
                    IReadOnlyList<Series> existing = await checkService.GetAllAsync();
                    forceImportAll = !existing.Any();
                }

                Progress<ScanProgress> progress = new(p =>
                {
                    SyncStatusText = !string.IsNullOrEmpty(p.PhaseLabel) ? p.PhaseLabel : p.StatusText;
                    ScanDetailText = p.DetailText ?? string.Empty;
                    IsScanIndeterminate = p.PercentComplete <= 0;
                    ScanProgressPercent = p.PercentComplete;
                    _statusBar.UpdateScanProgress(p);
                });

                SyncResult result = await _syncService.SyncAsync(progress, forceImportAll: forceImportAll);

                // Fortschrittsbalken sofort ausblenden – bevor der Reload die Liste neu befüllt.
                // Sonst wirkt die Ladezeit der DB-Ansicht wie ein "hängengebliebener" Scan.
                _statusBar.ClearScanProgress();
                SyncStatusText = $"Scan abgeschlossen: {result.TracksCreated} Tracks angelegt, {result.EpisodesUpdated} Episoden aktualisiert";
                ScanDetailText = string.Empty;

                await RaiseLibraryReloadedAsync();
            }
            catch (Exception ex)
            {
                _statusBar.ClearScanProgress();
                SyncStatusText = string.Empty;
                ScanDetailText = string.Empty;
                await _errorDialogService.ShowAsync(SafeResourceLoader.Get("LibraryScanFailedTitle"), ex.Message);
            }
            finally
            {
                IsScanning = false;
            }
        }

        /// <summary>
        /// Setzt die lokale Bibliothek vollständig zurück und startet einen Neu-Scan.
        /// Rein lokale Serien (ohne Online-ID) werden per Soft-Delete entfernt.
        /// Online-importierte Serien behalten ihre Metadaten – nur der lokale Pfad und
        /// die Track-Zuordnungen werden gelöscht. Wiedergabestatus bleibt in jedem Fall erhalten.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Neuinitialisierung vom Command: DB-/IO-Fehler beim Leeren und Neuanlegen der Bibliothek dürfen den Command nicht reißen; Fehler werden als Status angezeigt.")]
        private async Task ReInitializeAsync()
        {
            bool confirmed = await _confirmationDialogService.ConfirmAsync(
                SafeResourceLoader.Get("LibraryReinitTitle"),
                SafeResourceLoader.Get("LibraryReinitMessage"));

            if (!confirmed)
            {
                return;
            }

            // Anzeige sofort leeren – der Nutzer sieht das Reset unmittelbar, bevor der lange DB-Cleanup beginnt.
            ScanStarting?.Invoke();

            IsScanning = true;
            IsScanIndeterminate = true;
            ScanProgressPercent = 0;
            ScanDetailText = string.Empty;
            SyncStatusText = SafeResourceLoader.Get("ScanStatusPreparing");

            try
            {
                // Datenbankbereinigung auf dem Threadpool – verhindert UI-Freeze durch die vielen
                // aufeinanderfolgenden DB-Awaits. Ein eigener Scope ist nötig, da DbContext nicht
                // thread-sicher ist.
                await Task.Run(async () =>
                {
                    using IServiceScope cleanupScope = _scopeFactory.CreateScope();
                    ISeriesDataService seriesService = cleanupScope.ServiceProvider.GetRequiredService<ISeriesDataService>();
                    IEpisodeDataService episodeService = cleanupScope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
                    ILocalTrackDataService trackService = cleanupScope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();
                    ICoverImageDataService coverImageService = cleanupScope.ServiceProvider.GetRequiredService<ICoverImageDataService>();

                    IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync();

                    List<Guid> seriesIdsToResetCover = [];
                    List<Guid> episodeIdsToResetCover = [];

                    foreach (Series series in allSeries)
                    {
                        bool isLocalOnly = series.SpotifyArtistId is null && series.AppleMusicArtistId is null;

                        if (isLocalOnly)
                        {
                            // Rein lokale Serie vollständig entfernen – kaskadiert Episoden, Tracks und PlaybackStates
                            await seriesService.DeleteAsync(series.Id);
                            continue;
                        }

                        // Online-importierte Serie: lokale Zuordnung entfernen, Metadaten behalten
                        series.LocalFolderPath = null;
                        await seriesService.UpdateAsync(series);
                        seriesIdsToResetCover.Add(series.Id);

                        IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(series.Id);

                        foreach (Episode episode in episodes)
                        {
                            episode.LocalFolderPath = null;
                            episode.LocalTrackCount = null;
                            // Zurück auf den Ausgangszustand – kein lokaler Abgleich mehr vorhanden
                            episode.TrackMatchKind = TrackMatchKind.NotMatched;
                            await episodeService.UpdateAsync(episode);

                            episodeIdsToResetCover.Add(episode.Id);

                            // Leere Liste löscht alle LocalTrack-Einträge dieser Episode
                            await trackService.SaveTracksForEpisodeAsync(episode.Id, []);
                        }
                    }

                    // Gespeicherte Cover löschen – beim nächsten Scan werden sie neu eingelesen.
                    // Ohne dieses Reset würde das alte Binärbild dauerhaft in der CoverImages-Tabelle
                    // verbleiben, selbst wenn der Nutzer ein anderes Bild auf die Festplatte legt.
                    _ = await coverImageService.DeleteByEntitiesAsync(CoverEntityTypes.Series, seriesIdsToResetCover);
                    _ = await coverImageService.DeleteByEntitiesAsync(CoverEntityTypes.Episode, episodeIdsToResetCover);
                });

                string resettingStatus = SafeResourceLoader.Get("ScanStatusResetting");
                SyncStatusText = resettingStatus;
                _statusBar.UpdateScanProgress(new ScanProgress { StatusText = resettingStatus });

                Progress<ScanProgress> progress = new(p =>
                {
                    SyncStatusText = !string.IsNullOrEmpty(p.PhaseLabel) ? p.PhaseLabel : p.StatusText;
                    ScanDetailText = p.DetailText ?? string.Empty;
                    IsScanIndeterminate = p.PercentComplete <= 0;
                    ScanProgressPercent = p.PercentComplete;
                    _statusBar.UpdateScanProgress(p);
                });

                // forceImportAll: true – nach dem Reset müssen alle Serienordner neu importiert werden,
                // unabhängig davon ob AutoImportAfterScan in den Einstellungen aktiviert ist.
                SyncResult result = await _syncService.SyncAsync(progress, forceImportAll: true);

                _statusBar.ClearScanProgress();
                SyncStatusText = $"Neu-Initialisierung abgeschlossen: {result.TracksCreated} Tracks angelegt";
                ScanDetailText = string.Empty;

                await RaiseLibraryReloadedAsync();
            }
            catch (Exception ex)
            {
                _statusBar.ClearScanProgress();
                SyncStatusText = string.Empty;
                ScanDetailText = string.Empty;
                await _errorDialogService.ShowAsync(SafeResourceLoader.Get("LibraryReinitFailedTitle"), ex.Message);
            }
            finally
            {
                IsScanning = false;
            }
        }

        // ── Ordner wählen ────────────────────────────────────────────────────────

        /// <summary>
        /// Öffnet den System-Ordnerpicker, speichert den gewählten Pfad in AppSettings
        /// und meldet danach <see cref="LibraryReloaded"/>, damit das übergeordnete
        /// ViewModel die Ansicht aktualisiert. Bricht der Benutzer ab, bleibt der bisherige Zustand erhalten.
        /// </summary>
        /// <param name="windowHandle">
        /// HWND des Hauptfensters – WinRT-FolderPicker muss per <c>InitializeWithWindow</c>
        /// an ein Fenster gebunden werden.
        /// </param>
        public async Task PickFolderAsync(nint windowHandle)
        {
            Windows.Storage.Pickers.FolderPicker picker = new();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
            picker.FileTypeFilter.Add("*");

            Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();

            if (folder is null)
            {
                return;
            }

            // Pfad dauerhaft speichern, damit er beim nächsten Start noch vorhanden ist
            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
            EchoPlay.Data.Entities.Settings.AppSettings settings = await settingsService.GetAsync();
            settings.LocalLibraryRootPath = folder.Path;
            await settingsService.SaveAsync(settings);

            await RaiseLibraryReloadedAsync();
        }

        // ── Ordner manuell hinzufügen ────────────────────────────────────────────

        /// <summary>
        /// Öffnet den Ordnerpicker, legt den gewählten Ordner als neue lokale Serie an
        /// und meldet danach <see cref="LibraryReloaded"/>. Wurde der Ordner bereits als Serienordner
        /// zugeordnet, wird kein Duplikat erstellt – der Nutzer erhält stattdessen einen stillen Abbruch.
        /// </summary>
        /// <param name="windowHandle">
        /// HWND des Hauptfensters – WinRT-FolderPicker muss per <c>InitializeWithWindow</c>
        /// an ein Fenster gebunden werden.
        /// </param>
        public async Task AddFolderAsync(nint windowHandle)
        {
            Windows.Storage.Pickers.FolderPicker picker = new();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
            picker.FileTypeFilter.Add("*");

            Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();

            if (folder is null)
            {
                return;
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();

            // Bestehende Serien laden, um Duplikate per Ordnerpfad zu erkennen
            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync();

            bool pathAlreadyUsed = allSeries.Any(
                s => string.Equals(s.LocalFolderPath, folder.Path, StringComparison.OrdinalIgnoreCase));

            if (pathAlreadyUsed)
            {
                return;
            }

            Series newSeries = new()
            {
                Title = folder.Name,
                LocalFolderPath = folder.Path
            };

            await seriesService.AddAsync(newSeries);
            await RaiseLibraryReloadedAsync();
        }

        /// <summary>
        /// Löst <see cref="LibraryReloaded"/> aus, falls ein Abonnement vorhanden ist.
        /// Ohne Abonnenten wird der Aufruf stillschweigend übersprungen – relevant für Tests,
        /// die den Scan ohne angehängtes Top-ViewModel ausführen.
        /// </summary>
        private async Task RaiseLibraryReloadedAsync()
        {
            Func<Task>? handler = LibraryReloaded;
            if (handler is not null)
            {
                await handler.Invoke();
            }
        }

        /// <summary>
        /// Trennt das Event-Abonnement beim Abräumen des ViewModels.
        /// </summary>
        public void Dispose()
        {
            Deactivate();
        }
    }
}
