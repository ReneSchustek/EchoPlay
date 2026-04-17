using EchoPlay.App.Helpers;
using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.LocalLibrary.Analysis;
using EchoPlay.LocalLibrary.Scanning;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Sub-ViewModel für den Lokal-Tab der Einstellungsseite.
    /// Verwaltet Bibliothekspfad, Episodenordner-Muster, Mustererkennung, Auto-Import-Schalter
    /// und den manuellen Sync-Lauf gegen die lokale Bibliothek.
    /// </summary>
    public sealed class LocalSettingsViewModel : ObservableObject
    {
        private readonly ISyncService _syncService;
        private readonly IErrorDialogService _errorDialogService;
        private readonly IEpisodePatternAnalyzer _patternAnalyzer;
        private readonly Action _onUserEdit;

        private bool _isBatchLoading;
        private bool _localLibraryEnabled = true;
        private string? _localLibraryRootPath;
        private string _episodeFolderPattern = "{number:000} - {title}";
        private bool _autoImportAfterScan;
        private bool _isSyncing;
        private string _syncStatusText = string.Empty;
        private IReadOnlyList<PatternSuggestionDisplay> _patternSuggestionDisplays = [];

        /// <summary>
        /// Schwellwert für automatische Übernahme eines Musters ohne Nutzerrückfrage.
        /// Liegt die Trefferquote bei mindestens 85 %, ist das Ergebnis eindeutig genug.
        /// </summary>
        private const double HighConfidenceThreshold = 0.85;

        /// <summary>
        /// Initialisiert das Sub-VM mit den benötigten Diensten.
        /// </summary>
        /// <param name="syncService">Für den Sync-Lauf gegen die lokale Bibliothek.</param>
        /// <param name="errorDialogService">Für Fehler-Dialoge nach fehlgeschlagenem Sync.</param>
        /// <param name="patternAnalyzer">Analysiert den Bibliotheksordner auf Episodenmuster.</param>
        /// <param name="onUserEdit">Wird bei einer Nutzeränderung aufgerufen.</param>
        public LocalSettingsViewModel(
            ISyncService syncService,
            IErrorDialogService errorDialogService,
            IEpisodePatternAnalyzer patternAnalyzer,
            Action onUserEdit)
        {
            _syncService = syncService;
            _errorDialogService = errorDialogService;
            _patternAnalyzer = patternAnalyzer;
            _onUserEdit = onUserEdit;
        }

        /// <summary>
        /// Wird ausgelöst, wenn mehrere Muster-Vorschläge vorliegen oder das beste Ergebnis
        /// unter dem Konfidenz-Schwellwert liegt. Die Page zeigt dann einen Auswahl-Dialog.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "VM->Page-Signal mit Ergebnis-Liste: die Page oeffnet einen ContentDialog fuer die Muster-Auswahl; Action<IReadOnlyList<...>> bleibt klarer als ein dedizierter EventArgs-Typ mit identischer Semantik.")]
        public event Action<IReadOnlyList<PatternSuggestionDisplay>>? PatternSelectionRequested;

        /// <summary>Gibt an, ob die lokale Bibliothek aktiv ist.</summary>
        public bool LocalLibraryEnabled
        {
            get => _localLibraryEnabled;
            set
            {
                if (SetProperty(ref _localLibraryEnabled, value))
                {
                    MarkAsChanged();
                }
            }
        }

        /// <summary>
        /// Pfad zur lokalen Bibliothek. Leere Eingaben werden intern als <see langword="null"/> gespeichert.
        /// Aktualisiert <see cref="IsSyncEnabled"/>, da der Sync ohne Pfad blockiert ist.
        /// </summary>
        public string LocalLibraryRootPath
        {
            get => _localLibraryRootPath ?? string.Empty;
            set
            {
                if (SetProperty(ref _localLibraryRootPath, string.IsNullOrWhiteSpace(value) ? null : value))
                {
                    OnPropertyChanged(nameof(IsSyncEnabled));
                    MarkAsChanged();
                }
            }
        }

        /// <summary>Muster für die Benennung von Episodenordnern.</summary>
        public string EpisodeFolderPattern
        {
            get => _episodeFolderPattern;
            set
            {
                if (SetProperty(ref _episodeFolderPattern, value))
                {
                    MarkAsChanged();
                }
            }
        }

        /// <summary>
        /// Gibt an, ob erkannte Serien nach einem Scan automatisch importiert werden sollen.
        /// </summary>
        public bool AutoImportAfterScan
        {
            get => _autoImportAfterScan;
            set
            {
                if (SetProperty(ref _autoImportAfterScan, value))
                {
                    MarkAsChanged();
                }
            }
        }

        /// <summary>Gibt an, ob gerade ein Sync-Vorgang läuft.</summary>
        public bool IsSyncing
        {
            get => _isSyncing;
            private set
            {
                if (SetProperty(ref _isSyncing, value))
                {
                    OnPropertyChanged(nameof(IsSyncEnabled));
                }
            }
        }

        /// <summary>
        /// Gibt an, ob der Sync-Button aktiviert werden soll.
        /// Falsch während ein Sync läuft oder solange kein Bibliothekspfad konfiguriert ist.
        /// </summary>
        public bool IsSyncEnabled => !_isSyncing && !string.IsNullOrWhiteSpace(_localLibraryRootPath);

        /// <summary>Statustext des letzten oder laufenden Sync-Vorgangs.</summary>
        public string SyncStatusText
        {
            get => _syncStatusText;
            private set => SetProperty(ref _syncStatusText, value);
        }

        /// <summary>
        /// Vorschläge für das Episodenordner-Muster, ermittelt durch den <see cref="IEpisodePatternAnalyzer"/>.
        /// </summary>
        public IReadOnlyList<PatternSuggestionDisplay> PatternSuggestions
        {
            get => _patternSuggestionDisplays;
            private set
            {
                if (SetProperty(ref _patternSuggestionDisplays, value))
                {
                    OnPropertyChanged(nameof(PatternSuggestionsVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit der Vorschlagsliste – nur wenn mindestens ein Vorschlag vorhanden ist.
        /// </summary>
        public Visibility PatternSuggestionsVisibility =>
            _patternSuggestionDisplays.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Übernimmt den angegebenen Vorschlag als aktives Episodenmuster.
        /// </summary>
        /// <param name="pattern">Das zu übernehmende Muster-Pattern.</param>
        public void ApplyPatternSuggestion(string pattern)
        {
            EpisodeFolderPattern = pattern;
        }

        /// <summary>
        /// Analysiert den konfigurierten Bibliotheksordner und befüllt <see cref="PatternSuggestions"/>.
        /// Bei einem einzigen Treffer mit hoher Konfidenz wird das Muster direkt übernommen.
        /// Andernfalls wird <see cref="PatternSelectionRequested"/> ausgelöst.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task AnalyzePatternAsync()
        {
            if (string.IsNullOrWhiteSpace(_localLibraryRootPath))
            {
                PatternSuggestions = [];
                return;
            }

            IReadOnlyList<PatternSuggestion> suggestions = await _patternAnalyzer.AnalyzeAsync(_localLibraryRootPath);
            List<PatternSuggestionDisplay> displays = new(suggestions.Count);
            foreach (PatternSuggestion suggestion in suggestions)
            {
                displays.Add(new PatternSuggestionDisplay(
                    suggestion.Pattern,
                    suggestion.MatchCount,
                    suggestion.MatchPercentage,
                    suggestion.IsFlatStructure));
            }
            PatternSuggestions = displays;

            if (displays.Count == 1 && displays[0].MatchPercentage >= HighConfidenceThreshold)
            {
                // Eindeutiges Ergebnis – direkt übernehmen, kein Dialog nötig
                ApplyPatternSuggestion(displays[0].Pattern);
            }
            else if (displays.Count > 0)
            {
                // Mehrere Treffer oder unsicheres Ergebnis – Nutzer entscheiden lassen
                PatternSelectionRequested?.Invoke(displays);
            }
        }

        /// <summary>
        /// Startet den Sync der lokalen Bibliothek mit der Datenbank.
        /// Läuft ein Sync bereits oder ist kein Pfad konfiguriert, wird der Aufruf mit einem
        /// erklärenden Statustext beantwortet, ohne den Service zu belasten.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Manueller Bibliotheks-Sync ueber die Einstellungen: IO-/DB-/TagLib-Fehler werden in 'StatusMessage' als Nutzer-Fehlermeldung gespiegelt, damit der Sync-Command nicht abbricht.")]
        public async Task SyncAsync()
        {
            if (IsSyncing)
            {
                return;
            }

            // Vor dem Start prüfen – SyncService würde sonst lautlos ein leeres SyncResult zurückgeben
            if (!LocalLibraryEnabled)
            {
                SyncStatusText = SafeResourceLoader.Get("SyncDisabledHint", "Lokale Bibliothek ist deaktiviert.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_localLibraryRootPath))
            {
                SyncStatusText = SafeResourceLoader.Get("SyncNoPathHint", "Kein Bibliotheksordner konfiguriert.");
                return;
            }

            IsSyncing = true;
            SyncStatusText = SafeResourceLoader.Get("SyncRunning", "Sync läuft …");

            try
            {
                Progress<ScanProgress> progress = new(p => SyncStatusText = p.StatusText);
                SyncResult result = await _syncService.SyncAsync(progress);
                SyncStatusText = result.ToString();
            }
            catch (Exception ex)
            {
                string syncFailed = SafeResourceLoader.Get("SyncFailed", "Sync fehlgeschlagen");
                SyncStatusText = $"{syncFailed}: {ex.Message}";
                await _errorDialogService.ShowAsync(syncFailed, ex.Message);
            }
            finally
            {
                IsSyncing = false;
            }
        }

        /// <summary>
        /// Öffnet einen System-FolderPicker und setzt den Bibliothekspfad auf die gewählte Auswahl.
        /// </summary>
        /// <param name="windowHandle">HWND des Hauptfensters – der WinRT-FolderPicker muss per
        /// <c>InitializeWithWindow</c> an ein Fenster gebunden werden.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        public async Task BrowseLibraryFolderAsync(nint windowHandle)
        {
            FolderPicker picker = new();

            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
            picker.SuggestedStartLocation = PickerLocationId.MusicLibrary;

            // FileTypeFilter ist auch beim FolderPicker Pflicht – Wildcard genügt
            picker.FileTypeFilter.Add("*");

            StorageFolder? folder = await picker.PickSingleFolderAsync();

            if (folder is not null)
            {
                LocalLibraryRootPath = folder.Path;
            }
        }

        /// <summary>Übernimmt alle lokal-bezogenen Werte aus der Entität ohne Change-Callback.</summary>
        /// <param name="settings">Die geladene Entität.</param>
        public void LoadFrom(AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            _isBatchLoading = true;
            try
            {
                LocalLibraryEnabled = settings.LocalLibraryEnabled;
                LocalLibraryRootPath = settings.LocalLibraryRootPath ?? string.Empty;
                EpisodeFolderPattern = settings.EpisodeFolderPattern;
                AutoImportAfterScan = settings.AutoImportAfterScan;
            }
            finally
            {
                _isBatchLoading = false;
            }
        }

        /// <summary>Schreibt alle lokal-bezogenen Werte in die Entität.</summary>
        /// <param name="settings">Ziel-Entität.</param>
        public void WriteTo(AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            settings.LocalLibraryEnabled = LocalLibraryEnabled;
            settings.LocalLibraryRootPath = string.IsNullOrWhiteSpace(LocalLibraryRootPath) ? null : LocalLibraryRootPath;
            settings.EpisodeFolderPattern = EpisodeFolderPattern;
            settings.AutoImportAfterScan = AutoImportAfterScan;
        }

        private void MarkAsChanged()
        {
            if (!_isBatchLoading)
            {
                _onUserEdit();
            }
        }
    }
}
