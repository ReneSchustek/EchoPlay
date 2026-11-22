using EchoPlay.App.Helpers;
using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.LocalLibrary.Scanning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für die Scan- und Status-Logik der lokalen Mediathek.
    /// Kapselt den Ablauf von Scan und Neu-Initialisierung, verwaltet den Einrichtungszustand
    /// der Bibliothek und bedient den Ordnerpicker zum Auswählen oder Hinzufügen von Ordnern.
    /// Die eigentliche Anzeige der Serien übernimmt weiterhin <see cref="LocalLibraryViewModel"/>;
    /// diese Klasse meldet Fortschritt und Abschluss über <see cref="LibraryReloaded"/> und den
    /// Konstruktor-Callback für einzelne synchronisierte Serien zurück.
    /// </summary>
    public sealed class LocalLibraryScanViewModel : ObservableObject, IDisposable
    {
        private readonly ISyncService _syncService;
        private readonly IErrorDialogService _errorDialogService;
        private readonly IConfirmationDialogService _confirmationDialogService;
        private readonly StatusBarViewModel _statusBar;
        private readonly IScanEventService _scanEventService;
        private readonly Action<Series> _onSeriesSynced;

        // Löst die Verbindung zwischen Datenbank und Festplatte, bevor neu eingelesen wird.
        private readonly LocalLibraryReset _libraryReset;

        // Schreibt die Wahl aus dem Ordnerdialog in Einstellungen und Bestand.
        private readonly LocalLibraryFolderStore _folderStore;

        /// <summary>
        /// DispatcherQueue des UI-Threads; wird im Konstruktor auf dem UI-Thread erfasst und
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
            ArgumentNullException.ThrowIfNull(scopeFactory);
            _syncService = syncService;
            _errorDialogService = errorDialogService;
            _confirmationDialogService = confirmationDialogService;
            _statusBar = statusBar;
            _scanEventService = scanEventService;
            _onSeriesSynced = onSeriesSynced;
            _libraryReset = new LocalLibraryReset(scopeFactory);
            _folderStore = new LocalLibraryFolderStore(scopeFactory);

            // DispatcherQueue auf dem UI-Thread erfassen, denn Events des Scan-Dienstes treffen später
            // vom Hintergrundthread ein und müssen auf den UI-Thread gemarshallt werden.
            // In Unit-Tests existiert kein WinUI-3-Dispatcher; dort bleibt _dispatcherQueue null.
            _dispatcherQueue = UiDispatcher.TryGetForCurrentThread();

            ScanCommand = new RelayCommand(() => _ = ScanAsync());
            ReInitializeCommand = new RelayCommand(() => _ = ReInitializeAsync());
            AddFolderCommand = new RelayCommand(() => AddFolderRequested?.Invoke());
        }

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
        /// Sichtbarkeit des Scan-Overlays, eingeblendet, während ein Scan oder eine Neu-Initialisierung läuft.
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

        /// <summary>Startet einen neuen Scan der lokalen Bibliothek.</summary>
        public ICommand ScanCommand { get; }

        /// <summary>Setzt alle lokalen Zuordnungen zurück und scannt neu.</summary>
        public ICommand ReInitializeCommand { get; }

        /// <summary>
        /// Fordert das Hinzufügen eines Ordners an. Die Page liefert über
        /// <see cref="AddFolderRequested"/> das HWND des Hauptfensters nach.
        /// </summary>
        public ICommand AddFolderCommand { get; }

        /// <summary>
        /// Wird ausgelöst, wenn der Nutzer den Ordner-Hinzufügen-Button drückt.
        /// Die Page reagiert auf das Event und ruft <see cref="AddFolderAsync"/> mit dem Fenster-Handle auf.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Der Typ ist nur öffentlich, weil WinUI-Bindungen öffentliche Typen verlangen; einen Abonnenten außerhalb der Anwendung gibt es nicht. Gleich gebaute Ereignisse in internen Klassen bleiben bei Action — ein EventArgs-Typ allein an den öffentlichen Stellen führte ein zweites Muster ein, ohne dass jemand davon profitiert.")]
        public event Action? AddFolderRequested;

        /// <summary>
        /// Wird ausgelöst, sobald der Bibliotheksinhalt aus der Datenbank neu geladen werden muss,
        /// z.B. nach Abschluss eines Scans, einer Neu-Initialisierung oder einem Ordnerpicker-Ergebnis.
        /// Das übergeordnete ViewModel reagiert darauf und füllt Kacheln, Episoden und Tracks neu.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Das übergeordnete Ansichtsmodell wartet auf seine asynchronen Abonnenten, bevor es weiterarbeitet; EventHandler<T> liefert keinen Task, auf den es warten könnte.")]
        public event Func<Task>? LibraryReloaded;

        /// <summary>
        /// Wird unmittelbar vor Beginn eines Scans oder einer Neu-Initialisierung ausgelöst.
        /// Das übergeordnete ViewModel leert daraufhin seine Listen, damit der Nutzer während
        /// des Scans nicht auf veraltete Kacheln blickt.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Der Typ ist nur öffentlich, weil WinUI-Bindungen öffentliche Typen verlangen; einen Abonnenten außerhalb der Anwendung gibt es nicht. Gleich gebaute Ereignisse in internen Klassen bleiben bei Action — ein EventArgs-Typ allein an den öffentlichen Stellen führte ein zweites Muster ein, ohne dass jemand davon profitiert.")]
        public event Action? ScanStarting;

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
        /// ViewModel benachrichtigt wird. Nötig ist der Wechsel wegen <c>BitmapImage</c>: Als
        /// <c>DependencyObject</c> aus <c>Microsoft.UI.*</c> gehört es dem Thread, auf dem es
        /// entsteht. Die WinRT-Typen aus <c>Windows.*</c> sind dagegen systemweit registriert
        /// und brauchen den UI-Thread nicht.
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

        /// <summary>
        /// Startet einen Bibliotheks-Scan über den SyncService.
        /// Serien werden über <see cref="IScanEventService.SeriesSynced"/> sofort sichtbar gemacht;
        /// nach Abschluss meldet das Sub-VM über <see cref="LibraryReloaded"/>, damit das
        /// übergeordnete ViewModel Episodenzähler und Cover konsistent neu lädt.
        /// </summary>
        private async Task ScanAsync()
        {
            if (IsScanning)
            {
                return;
            }

            await RunScanAsync(
                async () =>
                {
                    // Leere DB (z.B. nach "Bibliothek zurücksetzen") zwingt den Scan in den
                    // Vollimport-Modus, unabhängig von AutoImportAfterScan in den Einstellungen.
                    bool forceImportAll = await _folderStore.IsLibraryEmptyAsync();
                    SyncResult result = await _syncService.SyncAsync(CreateScanProgress(), forceImportAll: forceImportAll);

                    string scanDone = SafeResourceLoader.Get("ScanStatusCompleted", "Scan abgeschlossen: {0} Tracks angelegt, {1} Episoden aktualisiert");
                    return string.Format(CultureInfo.CurrentCulture, scanDone, result.TracksCreated, result.EpisodesUpdated);
                },
                "LibraryScanFailedTitle",
                DialogKey.LibraryScanFailed);
        }

        /// <summary>
        /// Setzt die lokale Bibliothek vollständig zurück und startet einen Neu-Scan.
        /// Rein lokale Serien (ohne Online-ID) werden per Soft-Delete entfernt.
        /// Online-importierte Serien behalten ihre Metadaten, nur der lokale Pfad und
        /// die Track-Zuordnungen werden gelöscht. Wiedergabestatus bleibt in jedem Fall erhalten.
        /// </summary>
        private async Task ReInitializeAsync()
        {
            bool confirmed = await _confirmationDialogService.ConfirmAsync(
                SafeResourceLoader.Get("LibraryReinitTitle"),
                SafeResourceLoader.Get("LibraryReinitMessage"),
                DialogKey.LibraryReinit);

            if (!confirmed)
            {
                return;
            }

            await RunScanAsync(
                async () =>
                {
                    await _libraryReset.ClearLocalAssignmentsAsync();

                    string resettingStatus = SafeResourceLoader.Get("ScanStatusResetting");
                    SyncStatusText = resettingStatus;
                    _statusBar.ScanProgress.Update(new ScanProgress { StatusText = resettingStatus });

                    // forceImportAll: true, denn nach dem Reset müssen alle Serienordner neu importiert werden,
                    // unabhängig davon ob AutoImportAfterScan in den Einstellungen aktiviert ist.
                    SyncResult result = await _syncService.SyncAsync(CreateScanProgress(), forceImportAll: true);

                    string reinitDone = SafeResourceLoader.Get("ReinitStatusCompleted", "Neu-Initialisierung abgeschlossen: {0} Tracks angelegt");
                    return string.Format(CultureInfo.CurrentCulture, reinitDone, result.TracksCreated);
                },
                "LibraryReinitFailedTitle",
                DialogKey.LibraryReinitFailed);
        }

        /// <summary>
        /// Der gemeinsame Rahmen von Scan und Neu-Initialisierung: Listen leeren, Fortschritt
        /// zeigen, am Ende das Ergebnis melden und die Bibliothek neu laden, im Fehlerfall einen
        /// Dialog zeigen.
        /// </summary>
        /// <param name="run">Der eigentliche Durchlauf; liefert die Abschlussmeldung.</param>
        /// <param name="failedTitleKey">Ressourcenschlüssel des Titels im Fehlerdialog.</param>
        /// <param name="failedDialog">Kennung des Fehlerdialogs.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Scan und Neuinitialisierung vom Command: IO-/DB-/TagLib-Fehler, die bis hierher durchschlagen, erscheinen als Fehlerdialog, damit der Command nicht reißt.")]
        private async Task RunScanAsync(Func<Task<string>> run, string failedTitleKey, DialogKey failedDialog)
        {
            // Listen des übergeordneten ViewModels leeren, denn veraltete Kacheln dürfen nicht stehen bleiben.
            ScanStarting?.Invoke();

            IsScanning = true;
            IsScanIndeterminate = true;
            ScanProgressPercent = 0;
            ScanDetailText = string.Empty;
            SyncStatusText = SafeResourceLoader.Get("ScanStatusPreparing");

            try
            {
                string doneText = await run();

                // Fortschrittsbalken sofort ausblenden, bevor der Reload die Liste neu befüllt.
                // Sonst wirkt die Ladezeit der DB-Ansicht wie ein "hängengebliebener" Scan.
                _statusBar.ScanProgress.Clear();
                SyncStatusText = doneText;
                ScanDetailText = string.Empty;

                await RaiseLibraryReloadedAsync();
            }
            catch (Exception ex)
            {
                _statusBar.ScanProgress.Clear();
                SyncStatusText = string.Empty;
                ScanDetailText = string.Empty;
                await _errorDialogService.ShowAsync(SafeResourceLoader.Get(failedTitleKey), ex.Message, failedDialog);
            }
            finally
            {
                IsScanning = false;
            }
        }

        /// <summary>
        /// Öffnet den System-Ordnerpicker, speichert den gewählten Pfad in AppSettings
        /// und meldet danach <see cref="LibraryReloaded"/>, damit das übergeordnete
        /// ViewModel die Ansicht aktualisiert. Bricht der Benutzer ab, bleibt der bisherige Zustand erhalten.
        /// </summary>
        /// <param name="windowHandle">
        /// HWND des Hauptfensters; der WinRT-FolderPicker muss per <c>InitializeWithWindow</c>
        /// an ein Fenster gebunden werden.
        /// </param>
        public async Task PickFolderAsync(nint windowHandle)
        {
            Windows.Storage.StorageFolder? folder = await PickFolderFromUserAsync(windowHandle);
            if (folder is null)
            {
                return;
            }

            await _folderStore.SaveRootPathAsync(folder.Path);
            await RaiseLibraryReloadedAsync();
        }

        /// <summary>
        /// Öffnet den Ordnerpicker, legt den gewählten Ordner als neue lokale Serie an
        /// und meldet danach <see cref="LibraryReloaded"/>. Wurde der Ordner bereits als Serienordner
        /// zugeordnet, wird kein Duplikat erstellt, der Nutzer erhält stattdessen einen stillen Abbruch.
        /// </summary>
        /// <param name="windowHandle">
        /// HWND des Hauptfensters; der WinRT-FolderPicker muss per <c>InitializeWithWindow</c>
        /// an ein Fenster gebunden werden.
        /// </param>
        public async Task AddFolderAsync(nint windowHandle)
        {
            Windows.Storage.StorageFolder? folder = await PickFolderFromUserAsync(windowHandle);
            if (folder is null || !await _folderStore.TryAddSeriesFolderAsync(folder.Name, folder.Path))
            {
                return;
            }

            await RaiseLibraryReloadedAsync();
        }

        /// <summary>
        /// Spiegelt den gemeldeten Stand eines Durchlaufs in Overlay und Statusleiste.
        /// Scan und Neu-Initialisierung zeigen ihren Fortschritt gleich.
        /// </summary>
        private Progress<ScanProgress> CreateScanProgress() => new(p =>
        {
            SyncStatusText = !string.IsNullOrEmpty(p.PhaseLabel) ? p.PhaseLabel : p.StatusText;
            ScanDetailText = p.DetailText ?? string.Empty;
            IsScanIndeterminate = p.PercentComplete <= 0;
            ScanProgressPercent = p.PercentComplete;
            _statusBar.ScanProgress.Update(p);
        });

        /// <summary>
        /// Lässt den Nutzer einen Ordner wählen, beginnend in der Musikbibliothek.
        /// </summary>
        /// <param name="windowHandle">
        /// HWND des Hauptfensters; der WinRT-FolderPicker muss per <c>InitializeWithWindow</c>
        /// an ein Fenster gebunden werden.
        /// </param>
        /// <returns>Der gewählte Ordner, oder <see langword="null"/> bei Abbruch.</returns>
        private static async Task<Windows.Storage.StorageFolder?> PickFolderFromUserAsync(nint windowHandle)
        {
            Windows.Storage.Pickers.FolderPicker picker = new();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
            picker.FileTypeFilter.Add("*");

            return await picker.PickSingleFolderAsync();
        }

        /// <summary>
        /// Löst <see cref="LibraryReloaded"/> aus, falls ein Abonnement vorhanden ist.
        /// Ohne Abonnenten wird der Aufruf stillschweigend übersprungen (relevant für Tests,
        /// die den Scan ohne angehängtes Top-ViewModel ausführen).
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
