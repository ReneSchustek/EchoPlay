using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Core.Models.Import;
using EchoPlay.Core.Search;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// ViewModel für die Suche nach Hörspielserien.
    /// Koordiniert Online-Suchanfragen via <see cref="ImportService"/> sowie lokale Suche
    /// in der eigenen Datenbank, prüft den Import-Status jedes Ergebnisses und
    /// stellt die Ergebnisliste für die UI bereit.
    /// Der aktive Suchbereich (Online, Lokal, Beide) wird über <see cref="SelectedScopeIndex"/> gesteuert.
    /// </summary>
    public sealed class SucheViewModel : ObservableObject
    {
        private readonly ImportService _importService;
        private readonly IErrorDialogService _errorDialogService;
        private readonly ILocalizationService _localizationService;

        // Wird nur für lokale Suche benötigt – in Tests ohne lokale Serien kann null übergeben werden
        private readonly IServiceScopeFactory? _scopeFactory;

        // Zentraler Navigationsdienst – in Unit-Tests optional, damit der Ctor testbar bleibt
        private readonly INavigationService? _navigationService;

        private string _searchText = string.Empty;
        private bool _isLoading;
        private bool _hasSearched;
        private bool _isOnboardingHintVisible;
        private bool _showSuccessHint;
        private int _selectedScopeIndex;
        private IReadOnlyList<SearchResultViewModel> _results = [];

        /// <summary>
        /// Initialisiert das ViewModel mit den benötigten Services.
        /// </summary>
        /// <param name="importService">Koordiniert Suche und Import über externe Provider.</param>
        /// <param name="errorDialogService">Zeigt Fehler als Dialog an.</param>
        /// <param name="scopeFactory">
        /// Optionale Scope-Fabrik für die lokale Datenbanksuche.
        /// Wenn <see langword="null"/>, ist lokale Suche deaktiviert und gibt immer eine leere Liste zurück.
        /// </param>
        /// <param name="localizationService">Für lokalisierte Dialog-Texte.</param>
        /// <param name="navigationService">
        /// Optionaler Navigationsdienst. Nur nötig wenn das ViewModel aus der echten App-Shell
        /// heraus navigieren können soll (z. B. GoBack bei Offline-Modus, Wechsel zur Online-Mediathek).
        /// In Unit-Tests kann der Parameter weggelassen werden.
        /// </param>
        public SucheViewModel(
            ImportService importService,
            IErrorDialogService errorDialogService,
            ILocalizationService localizationService,
            IServiceScopeFactory? scopeFactory = null,
            INavigationService? navigationService = null)
        {
            _importService       = importService;
            _errorDialogService  = errorDialogService;
            _localizationService = localizationService;
            _scopeFactory        = scopeFactory;
            _navigationService   = navigationService;

            SearchCommand = new RelayCommand(() => _ = SearchAsync());
            ResetCommand  = new RelayCommand(Reset);
        }

        /// <summary>
        /// Wird beim Betreten der Seite aufgerufen. Prüft den Offline-Modus, verarbeitet den
        /// Navigationsparameter (<c>"onboarding"</c> oder ein Suchtext) und löst ggf. eine Suche aus.
        /// Bei aktivem Offline-Modus wird ein Hinweis gezeigt und zurück zur vorherigen Seite navigiert.
        /// </summary>
        /// <param name="parameter">Navigationsparameter der Page – meist ein String oder <see langword="null"/>.</param>
        public async Task InitializeAsync(object? parameter)
        {
            if (await IsOfflineModeBlockingAsync())
            {
                await _errorDialogService.ShowAsync(
                    _localizationService.Get("OfflineModeSearchHintTitle"),
                    _localizationService.Get("OfflineModeSearchHintMessage"));
                _navigationService?.GoBack();
                return;
            }

            if (parameter is not string text || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (text == "onboarding")
            {
                IsOnboardingHintVisible = true;
                return;
            }

            SearchText = text;

            if (SearchCommand.CanExecute(null))
            {
                SearchCommand.Execute(null);
            }
        }

        /// <summary>
        /// Wird aus dem Erfolgshinweis gerufen: navigiert zur Online-Mediathek.
        /// </summary>
        public void NavigateToOnlineMediathek()
        {
            _navigationService?.NavigateTo(NavigationTarget.MediathekOnline);
        }

        /// <summary>
        /// Prüft anhand der AppSettings, ob der Offline-Modus aktiv ist und die Suche damit blockiert wird.
        /// In Tests ohne Scope-Factory gibt die Methode <see langword="false"/> zurück.
        /// </summary>
        private async Task<bool> IsOfflineModeBlockingAsync()
        {
            if (_scopeFactory is null)
            {
                return false;
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService = scope.ServiceProvider
                .GetRequiredService<IAppSettingsDataService>();
            AppSettings settings = await settingsService.GetAsync();
            return settings.OfflineMode;
        }

        /// <summary>
        /// Steuert den Onboarding-Hinweis, der beim allerersten Start erscheint.
        /// Wird auf <see langword="true"/> gesetzt, wenn die Seite mit dem Parameter "onboarding"
        /// aufgerufen wird – also wenn noch keine Serien in der Datenbank vorhanden sind.
        /// Verschwindet automatisch, sobald eine Suche gestartet wird.
        /// </summary>
        public bool IsOnboardingHintVisible
        {
            get => _isOnboardingHintVisible;
            set
            {
                if (SetProperty(ref _isOnboardingHintVisible, value))
                {
                    OnPropertyChanged(nameof(OnboardingHintVisibility));
                }
            }
        }

        /// <summary>
        /// Sichtbarkeit des Onboarding-Hinweistexts.
        /// <see cref="Visibility.Visible"/> solange noch keine Suche läuft oder ausgeführt wurde
        /// und der Onboarding-Modus aktiv ist.
        /// </summary>
        public Visibility OnboardingHintVisibility =>
            _isOnboardingHintVisible && !_hasSearched && !_isLoading
                ? Visibility.Visible
                : Visibility.Collapsed;

        /// <summary>
        /// Sucheingabe des Nutzers. Wird im TwoWay-Binding mit der AutoSuggestBox verknüpft.
        /// </summary>
        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        /// <summary>
        /// Index des aktiven Suchbereichs in der Scope-ComboBox der UI.
        /// 0 = Alle Quellen (Standard), 1 = Online, 2 = Lokal.
        /// </summary>
        public int SelectedScopeIndex
        {
            get => _selectedScopeIndex;
            set => SetProperty(ref _selectedScopeIndex, value);
        }

        /// <summary>Gibt an, ob gerade eine Suchanfrage läuft.</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    OnPropertyChanged(nameof(EmptyStateVisibility));
                    OnPropertyChanged(nameof(LoadingVisibility));
                }
            }
        }

        /// <summary>Suchergebnisse der letzten Anfrage.</summary>
        public IReadOnlyList<SearchResultViewModel> Results
        {
            get => _results;
            private set
            {
                if (SetProperty(ref _results, value))
                {
                    OnPropertyChanged(nameof(EmptyStateVisibility));
                }
            }
        }

        /// <summary>
        /// Steuert den Lade-Indikator. Sichtbar während eine Anfrage läuft.
        /// </summary>
        public Visibility LoadingVisibility =>
            _isLoading ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Wird eingeblendet, wenn eine Suche abgeschlossen ist und keine Ergebnisse vorliegen.
        /// Verhindert, dass der leere Zustand beim ersten Aufruf der Seite angezeigt wird.
        /// </summary>
        public Visibility EmptyStateVisibility =>
            _hasSearched && !_isLoading && _results.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        /// <summary>Startet eine neue Suchanfrage mit dem aktuellen Suchtext.</summary>
        public ICommand SearchCommand { get; }

        /// <summary>Setzt Suchfeld und Ergebnisse zurück.</summary>
        public ICommand ResetCommand { get; }

        /// <summary>
        /// Sichtbarkeit des Erfolgshinweises nach dem Hinzufügen einer Serie.
        /// Wird von <see cref="SearchResultViewModel"/> über <see cref="NotifySeriesAdded"/> gesetzt.
        /// </summary>
        public bool ShowSuccessHint
        {
            get => _showSuccessHint;
            private set
            {
                if (SetProperty(ref _showSuccessHint, value))
                {
                    OnPropertyChanged(nameof(SuccessHintVisibility));
                }
            }
        }

        /// <summary>Sichtbarkeit des Erfolgshinweises.</summary>
        public Visibility SuccessHintVisibility =>
            _showSuccessHint ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Wird von <see cref="SearchResultViewModel"/> nach erfolgreichem Hinzufügen aufgerufen.
        /// </summary>
        public void NotifySeriesAdded()
        {
            ShowSuccessHint = true;
        }

        /// <summary>
        /// Abgeleitet aus <see cref="SelectedScopeIndex"/>:
        /// 0 → <see cref="SearchSource.Both"/>, 1 → <see cref="SearchSource.Online"/>,
        /// 2 → <see cref="SearchSource.Local"/>.
        /// </summary>
        private SearchSource SelectedScope => _selectedScopeIndex switch
        {
            1 => SearchSource.Online,
            2 => SearchSource.Local,
            _ => SearchSource.Both
        };

        /// <summary>
        /// Führt die Suche gemäß dem aktiven Suchbereich durch, prüft den Import-Status
        /// jedes Ergebnisses und befüllt die <see cref="Results"/>-Liste.
        /// Online- und lokale Ergebnisse werden bei Bedarf zusammengeführt.
        /// </summary>
        private async Task SearchAsync()
        {
            if (_isLoading || string.IsNullOrWhiteSpace(_searchText))
            {
                return;
            }

            _hasSearched = false;
            IsLoading    = true;

            // Onboarding-Hinweis ausblenden, sobald der Nutzer aktiv sucht
            IsOnboardingHintVisible = false;

            SearchSource scope = SelectedScope;

            try
            {
                List<SearchResultViewModel> viewModels = [];

                // Online-Suche: via aktivem Provider (Spotify oder Apple Music)
                if (scope is SearchSource.Online or SearchSource.Both)
                {
                    // Parallel nach Serien (Artists) und Folgen (Alben) suchen
                    IReadOnlyList<ImportSeries> seriesResults = await _importService.SearchAsync(_searchText);
                    IReadOnlyList<ImportSeries> albumResults  = await _importService.SearchAlbumsAsync(_searchText);

                    // Zusammenführen und nach Relevanz sortieren:
                    // Treffer mit Suchbegriff im Titel/Künstler zuerst, dann nach Score
                    string searchLower = _searchText.ToLowerInvariant();
                    List<ImportSeries> combined = new(seriesResults.Count + albumResults.Count);
                    combined.AddRange(seriesResults);
                    combined.AddRange(albumResults);
                    combined.Sort((a, b) =>
                    {
                        bool aContains = a.Title.Contains(searchLower, StringComparison.OrdinalIgnoreCase)
                                      || (a.ArtistName?.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ?? false);
                        bool bContains = b.Title.Contains(searchLower, StringComparison.OrdinalIgnoreCase)
                                      || (b.ArtistName?.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ?? false);

                        if (aContains != bContains) return aContains ? -1 : 1;
                        return b.Score.CompareTo(a.Score);
                    });

                    foreach (ImportSeries series in combined)
                    {
                        bool alreadyImported = series.IsAlbumResult
                            ? false
                            : await _importService.IsAlreadyImportedAsync(series);
                        viewModels.Add(new SearchResultViewModel(series, alreadyImported, _importService, _errorDialogService, _localizationService, this));
                    }
                }

                // Lokale Suche: direkt aus der Datenbank – kein Netzwerk-Aufruf erforderlich
                if (scope is SearchSource.Local or SearchSource.Both)
                {
                    IReadOnlyList<ImportSeries> localResults = await SearchLocalAsync(_searchText);

                    foreach (ImportSeries series in localResults)
                    {
                        // Lokale Einträge existieren bereits in der DB – Import-Button wäre hier nicht sinnvoll
                        viewModels.Add(new SearchResultViewModel(series, true, _importService, _errorDialogService, _localizationService));
                    }
                }

                _hasSearched = true;
                Results      = viewModels;
            }
            catch (Exception ex)
            {
                await _errorDialogService.ShowAsync(
                    _localizationService.Get("OnlineSearchFailedTitle"), ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Durchsucht die lokale Bibliothek nach Serien, deren Titel den Suchbegriff enthält.
        /// Gibt eine leere Liste zurück, wenn kein <see cref="IServiceScopeFactory"/> verfügbar ist –
        /// also wenn das ViewModel ohne Scope-Factory instanziiert wurde (typisch in Tests).
        /// </summary>
        /// <param name="query">Der Suchbegriff – Groß-/Kleinschreibung wird ignoriert.</param>
        /// <returns>Gefundene lokale Serien als <see cref="ImportSeries"/> mit <c>Source = "Lokal"</c>.</returns>
        private async Task<IReadOnlyList<ImportSeries>> SearchLocalAsync(string query)
        {
            if (_scopeFactory is null)
            {
                return [];
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync();

            List<ImportSeries> localResults = [];

            foreach (Series series in allSeries)
            {
                if (series.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    // SourceSeriesId = Datenbank-GUID der Serie, damit der Eintrag eindeutig identifizierbar bleibt
                    localResults.Add(new ImportSeries
                    {
                        Title          = series.Title,
                        Source         = "Lokal",
                        SourceSeriesId = series.Id.ToString()
                    });
                }
            }

            return localResults;
        }

        /// <summary>
        /// Setzt Suchfeld, Ergebnisliste und Statusanzeigen zurück.
        /// </summary>
        private void Reset()
        {
            SearchText      = string.Empty;
            Results         = [];
            _hasSearched    = false;
            ShowSuccessHint = false;
            OnPropertyChanged(nameof(EmptyStateVisibility));
        }
    }
}
