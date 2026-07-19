using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Core.Models.Import;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// ViewModel für die Import-Seite.
    /// Koordiniert die Suche nach Serien und den Import über den <see cref="ImportService"/>.
    /// Gibt Fortschrittsmeldungen über die <see cref="StatusBarViewModel"/> an die Info-Leiste weiter.
    /// </summary>
    public sealed class ImportViewModel : ObservableObject
    {
        private readonly ImportService _importService;
        private readonly IErrorDialogService _errorDialogService;
        private readonly IOnlineAccessGuard _onlineAccessGuard;
        private readonly ILocalizationService _localizationService;
        private readonly StatusBarViewModel? _statusBar;

        private string _searchQuery = string.Empty;
        private IReadOnlyList<ImportResultViewModel> _results = [];
        private bool _isSearching;
        private bool _isImporting;
        private string _statusText = string.Empty;

        /// <summary>
        /// Wird ausgelöst, wenn eine Serie erfolgreich importiert wurde.
        /// Ermöglicht der Seite, anschließend zur Serienübersicht zurückzunavigieren.
        /// </summary>
        public event EventHandler? ImportSucceeded;

        /// <summary>
        /// Initialisiert das ViewModel.
        /// </summary>
        /// <param name="importService">Service für Suche und Import.</param>
        /// <param name="errorDialogService">Service für Fehler-Dialoge.</param>
        /// <param name="onlineAccessGuard">Prüft den Offline-Modus und zeigt bei Bedarf einen Bestätigungsdialog.</param>
        /// <param name="localizationService">Liefert lokalisierte UI-Strings aus den Ressourcendateien.</param>
        /// <param name="statusBar">
        /// StatusBar-ViewModel für Fortschrittsanzeige während des Imports.
        /// <see langword="null"/> ist erlaubt – dann entfällt die Fortschrittsanzeige (z.B. in Tests).
        /// </param>
        public ImportViewModel(ImportService importService, IErrorDialogService errorDialogService, IOnlineAccessGuard onlineAccessGuard, ILocalizationService localizationService, StatusBarViewModel? statusBar = null)
        {
            _importService = importService;
            _errorDialogService = errorDialogService;
            _onlineAccessGuard = onlineAccessGuard;
            _localizationService = localizationService;
            _statusBar = statusBar;
        }

        /// <summary>Suchbegriff für die Seriensuche.</summary>
        public string SearchQuery
        {
            get => _searchQuery;
            set => SetProperty(ref _searchQuery, value);
        }

        /// <summary>Suchergebnisse als Liste von Ergebnis-ViewModels.</summary>
        public IReadOnlyList<ImportResultViewModel> Results
        {
            get => _results;
            private set => SetProperty(ref _results, value);
        }

        /// <summary>Gibt an, ob gerade eine Suchanfrage läuft.</summary>
        public bool IsSearching
        {
            get => _isSearching;
            private set
            {
                if (SetProperty(ref _isSearching, value))
                {
                    OnPropertyChanged(nameof(IsSearchEnabled));
                }
            }
        }

        /// <summary>Gibt an, ob gerade ein Import läuft.</summary>
        public bool IsImporting
        {
            get => _isImporting;
            private set
            {
                if (SetProperty(ref _isImporting, value))
                {
                    OnPropertyChanged(nameof(IsSearchEnabled));
                }
            }
        }

        /// <summary>
        /// Gibt an, ob der Suchen-Button aktiviert werden soll.
        /// Falsch, solange ein Vorgang läuft.
        /// </summary>
        public bool IsSearchEnabled => !_isSearching && !_isImporting;

        /// <summary>
        /// Statustext für laufende Vorgänge oder Ergebnisse.
        /// Leer solange noch keine Aktion ausgelöst wurde.
        /// </summary>
        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        /// <summary>
        /// Sucht nach Hörspielserien beim aktiven Provider.
        /// </summary>
        /// <returns>Asynchrone Ausführung.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Provider-Suche im ImportViewModel: HTTP-/Parser-/Timeout-Fehler der Online-Provider (Spotify/AppleMusic) werden als Nutzer-Fehlermeldung gespiegelt, damit der Import-Workflow nicht abbricht.")]
        public async Task SearchAsync()
        {
            if (IsSearching || string.IsNullOrWhiteSpace(SearchQuery))
            {
                return;
            }

            using IDisposable userAction = EchoPlay.App.Services.UserActionScope.BeginUserAction("ImportSearch");

            // Offline-Modus: Nutzer fragen, ob trotzdem ins Internet gegangen werden soll
            using IDisposable? onlineScope = await _onlineAccessGuard.RequestOnlineAccessAsync();
            if (onlineScope is null) return;

            IsSearching = true;
            StatusText = _localizationService.Get("ImportSearchInProgress");
            Results = [];

            try
            {
                SearchOutcome outcome = await _importService.SearchAsync(SearchQuery);
                IReadOnlyList<ImportSeries> found = outcome.Results;

                List<ImportResultViewModel> rows = new(found.Count);

                foreach (ImportSeries series in found)
                {
                    bool alreadyImported = await _importService.IsAlreadyImportedAsync(series);

                    ImportResultViewModel row = new(
                        series,
                        alreadyImported,
                        new RelayCommand(() => _ = ImportAsync(series)));

                    rows.Add(row);
                }

                Results = rows;
                StatusText = found.Count == 0 ? _localizationService.Get("ImportNoResults") : string.Empty;
            }
            catch (Exception ex)
            {
                StatusText = string.Format(CultureInfo.CurrentCulture, _localizationService.Get("ImportError"), ex.Message);
                await _errorDialogService.ShowAsync(_localizationService.Get("ImportSearchFailedTitle"), ex.Message);
            }
            finally
            {
                IsSearching = false;
            }
        }

        /// <summary>
        /// Importiert eine Serie und feuert <see cref="ImportSucceeded"/> nach erfolgreichem Abschluss.
        /// Fortschrittsmeldungen werden sowohl in <see cref="StatusText"/> als auch in der
        /// globalen Info-Leiste (<see cref="StatusBarViewModel"/>) angezeigt.
        /// </summary>
        /// <param name="series">Die zu importierende Serie.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Import eines Serien-Datensatzes: HTTP-Fehler beim Cover-Download, DB-Concurrency-Fehler oder Provider-Parser-Probleme werden als Nutzer-Fehlermeldung angezeigt, damit der Nutzer den Import erneut anstossen kann.")]
        public async Task ImportAsync(ImportSeries series)
        {
            ArgumentNullException.ThrowIfNull(series);
            if (IsImporting)
            {
                return;
            }

            using IDisposable userAction = EchoPlay.App.Services.UserActionScope.BeginUserAction("ImportSeries");

            // Offline-Modus: Nutzer fragen, ob trotzdem ins Internet gegangen werden soll
            using IDisposable? onlineScope = await _onlineAccessGuard.RequestOnlineAccessAsync();
            if (onlineScope is null) return;

            IsImporting = true;
            StatusText = string.Format(CultureInfo.CurrentCulture, _localizationService.Get("ImportInProgress"), series.Title);

            try
            {
                // Progress-Callback leitet Meldungen gleichzeitig an die Seite und die Info-Leiste weiter
                Progress<string> progress = new(text =>
                {
                    StatusText = text;
                    _statusBar?.SetScanProgress(text);
                });

                _ = await _importService.ImportAsync(series, progress);
                StatusText = string.Format(CultureInfo.CurrentCulture, _localizationService.Get("ImportSuccess"), series.Title);
                ImportSucceeded?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                StatusText = string.Format(CultureInfo.CurrentCulture, _localizationService.Get("ImportError"), ex.Message);
                await _errorDialogService.ShowAsync(_localizationService.Get("ImportFailedTitle"), ex.Message);
            }
            finally
            {
                IsImporting = false;
                // Info-Leiste immer aufräumen, auch bei Fehlern
                _statusBar?.ClearScanProgress();
            }
        }
    }
}
