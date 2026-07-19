using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Core.Models.Import;
using EchoPlay.Core.Search;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    public sealed class SucheViewModel : ObservableObject, IDisposable
    {
        private readonly ImportService _importService;
        private readonly IErrorDialogService _errorDialogService;
        private readonly ILocalizationService _localizationService;

        // Wird nur für lokale Suche benötigt – in Tests ohne lokale Serien kann null übergeben werden
        private readonly IServiceScopeFactory? _scopeFactory;

        // Zentraler Navigationsdienst – in Unit-Tests optional, damit der Ctor testbar bleibt
        private readonly INavigationService? _navigationService;

        // Page-Mode-Guard prüft den Offline-Modus beim Betreten der Page; null in Tests
        private readonly IPageModeGuard? _pageModeGuard;

        // Zentrale Cover-Pipeline: ersetzt den direkten Provider-URL-Download in den
        // Trefferkarten und sorgt für DB-First-Lookup, Foreground-Priorität und Rate-Limiter.
        private readonly BackgroundCoverService? _backgroundCoverService;

        private string _searchText = string.Empty;
        private bool _isLoading;
        private bool _hasSearched;
        private bool _isOnboardingHintVisible;
        private bool _showSuccessHint;
        private bool _isSpotifyFallbackHintVisible;
        private int _selectedScopeIndex;
        private IReadOnlyList<SearchResultViewModel> _results = [];

        // Liste statt Single-TCS: bei Back-to-Back-Suchen laufen mehrere Aufrufe parallel,
        // die alle abgewartet werden müssen – der älteste verwirft seine Ergebnisse, der
        // neueste schreibt sie. Tests warten über WaitForSearchCompleteAsync auf alle.
        private readonly object _inflightSearchesLock = new();
        private readonly List<TaskCompletionSource<bool>> _inflightSearches = [];

        // Pro Suche neu erzeugt; canceln und disposen am Anfang der nächsten Suche oder
        // beim Reset/CancelPendingSearchCovers, damit alte Cover-Loads keine HTTP-Requests
        // für vergessene Treffer mehr starten und damit Treffer obsolet gewordener Suchen
        // nicht mehr in die UI geschrieben werden.
        private CancellationTokenSource? _searchCoversCts;

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
        /// heraus navigieren können soll (z. B. Wechsel zur Online-Mediathek nach erfolgreichem Import).
        /// In Unit-Tests kann der Parameter weggelassen werden.
        /// </param>
        /// <param name="pageModeGuard">
        /// Optionaler Page-Mode-Guard – prüft den Offline-Modus beim Betreten der Page.
        /// In Tests kann der Parameter weggelassen werden, dann wird der Check übersprungen.
        /// </param>
        /// <param name="backgroundCoverService">
        /// Optionale zentrale Cover-Pipeline. Wird an <see cref="SearchResultViewModel"/>
        /// weitergereicht; bei <see langword="null"/> bleiben die Trefferkacheln ohne Cover.
        /// </param>
        public SucheViewModel(
            ImportService importService,
            IErrorDialogService errorDialogService,
            ILocalizationService localizationService,
            IServiceScopeFactory? scopeFactory = null,
            INavigationService? navigationService = null,
            IPageModeGuard? pageModeGuard = null,
            BackgroundCoverService? backgroundCoverService = null)
        {
            _importService = importService;
            _errorDialogService = errorDialogService;
            _localizationService = localizationService;
            _scopeFactory = scopeFactory;
            _navigationService = navigationService;
            _pageModeGuard = pageModeGuard;
            _backgroundCoverService = backgroundCoverService;

            SearchCommand = new RelayCommand(() => _ = SearchAsync());
            ResetCommand = new RelayCommand(Reset);
        }

        /// <summary>
        /// Wird beim Betreten der Seite aufgerufen. Prüft den Offline-Modus, verarbeitet den
        /// Navigationsparameter (<c>"onboarding"</c> oder ein Suchtext) und löst ggf. eine Suche aus.
        /// Bei aktivem Offline-Modus wird ein Hinweis gezeigt und zurück zur vorherigen Seite navigiert.
        /// </summary>
        /// <param name="parameter">Navigationsparameter der Page – meist ein String oder <see langword="null"/>.</param>
        public async Task InitializeAsync(object? parameter)
        {
            if (_pageModeGuard is not null && !await _pageModeGuard.EnsureOnlineAccessAsync())
            {
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
        /// Beim Leerwerden (eingebauter X-Button der AutoSuggestBox oder vollständiges
        /// Löschen per Tastatur) löst der Setter automatisch <see cref="Reset"/> aus,
        /// damit Treffer und Status-Hinweise sofort verschwinden.
        /// </summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value) && string.IsNullOrWhiteSpace(value))
                {
                    Reset();
                }
            }
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

        /// <summary>
        /// Wartet auf den Abschluss aller aktuell laufenden Suchanfragen (für deterministische Tests).
        /// Snapshottet die Liste der laufenden Such-TCS, damit Back-to-Back-Suchen vollständig
        /// abgewartet werden können – auch dann, wenn die ältere Suche aufgrund eines Token-Cancels
        /// kurz vor dem Abschluss steht.
        /// </summary>
        internal Task WaitForSearchCompleteAsync()
        {
            Task[] tasks;
            lock (_inflightSearchesLock)
            {
                if (_inflightSearches.Count == 0)
                {
                    return Task.CompletedTask;
                }

                tasks = _inflightSearches.Select(tcs => tcs.Task).ToArray();
            }

            return Task.WhenAll(tasks);
        }

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
        /// Signalisiert, dass die zuletzt gelaufene Suche wegen fehlender Spotify-Credentials
        /// auf Apple Music zurückgefallen ist. UI zeigt eine InfoBar, solange der Treffer-Zustand hält.
        /// </summary>
        public bool IsSpotifyFallbackHintVisible
        {
            get => _isSpotifyFallbackHintVisible;
            private set
            {
                if (SetProperty(ref _isSpotifyFallbackHintVisible, value))
                {
                    OnPropertyChanged(nameof(SpotifyFallbackHintVisibility));
                }
            }
        }

        /// <summary>Sichtbarkeit des Spotify-Fallback-Hinweises für die XAML-Bindung.</summary>
        public Visibility SpotifyFallbackHintVisibility =>
            _isSpotifyFallbackHintVisible ? Visibility.Visible : Visibility.Collapsed;

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
        ///
        /// Reset-/Abbruch-Disziplin: Trefferliste und Status-Hinweise werden
        /// noch vor dem ersten <c>await</c> geleert, damit alte Karten verschwinden bevor
        /// der HTTP-Call startet. Der <see cref="StartNewCoverScope"/>-Token markiert
        /// zugleich obsolete Suchen – nach jedem Await prüft der Code, ob inzwischen eine
        /// neue Suche begonnen hat und verwirft dann die eigenen Ergebnisse.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Such-Command der Suche-Seite: Provider-HTTP-/Parser-/Timeout-Fehler werden als Nutzer-Status gespiegelt, damit der Command nicht reißt.")]
        private async Task SearchAsync()
        {
            if (string.IsNullOrWhiteSpace(_searchText))
            {
                return;
            }

            TaskCompletionSource<bool> completedSource = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_inflightSearchesLock)
            {
                _inflightSearches.Add(completedSource);
            }

            // Cancel + neuer CTS: aktuelle Suche wird zum Inhaber des Tokens, alle älteren
            // Suchen sehen ab hier IsCancellationRequested == true und verwerfen ihre Treffer.
            CancellationToken coverToken = StartNewCoverScope();

            // Sucheingabe einfrieren, damit nachträgliche Änderungen am Feld während
            // der laufenden Anfrage den Treffer-Build nicht verfälschen.
            string searchText = _searchText;
            SearchSource scope = SelectedScope;

            // Sofort-Reset der Trefferansicht VOR dem ersten Await – die UI zeigt sofort
            // Loader plus leere Liste, alte Karten verschwinden noch im selben Frame.
            _hasSearched = false;
            ReleaseCurrentResults();
            Results = [];
            ShowSuccessHint = false;
            IsOnboardingHintVisible = false;
            IsSpotifyFallbackHintVisible = false;
            IsLoading = true;

            try
            {
                List<SearchResultViewModel> viewModels = [];

                if (scope is SearchSource.Online or SearchSource.Both)
                {
                    SearchOutcome seriesOutcome = await _importService.SearchAsync(searchText);
                    if (coverToken.IsCancellationRequested) return;

                    SearchOutcome albumsOutcome = await _importService.SearchAlbumsAsync(searchText);
                    if (coverToken.IsCancellationRequested) return;

                    IsSpotifyFallbackHintVisible =
                        seriesOutcome.SpotifyFallbackApplied || albumsOutcome.SpotifyFallbackApplied;

                    IReadOnlyList<ImportSeries> seriesResults = seriesOutcome.Results;
                    IReadOnlyList<ImportSeries> albumResults = albumsOutcome.Results;

                    // Zusammenführen und nach Relevanz sortieren:
                    // Treffer mit Suchbegriff im Titel/Künstler zuerst, dann nach Score
                    string searchLower = searchText.ToUpperInvariant();
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
                        if (coverToken.IsCancellationRequested) return;

                        viewModels.Add(new SearchResultViewModel(
                            series, alreadyImported, _importService, _errorDialogService,
                            _localizationService, _backgroundCoverService,
                            parentViewModel: this, cancellationToken: coverToken));
                    }
                }

                if (scope is SearchSource.Local or SearchSource.Both)
                {
                    IReadOnlyList<ImportSeries> localResults = await SearchLocalAsync(searchText);
                    if (coverToken.IsCancellationRequested) return;

                    foreach (ImportSeries series in localResults)
                    {
                        // Lokale Einträge existieren bereits in der DB – Import-Button wäre hier nicht sinnvoll
                        viewModels.Add(new SearchResultViewModel(
                            series, true, _importService, _errorDialogService,
                            _localizationService, _backgroundCoverService,
                            cancellationToken: coverToken));
                    }
                }

                _hasSearched = true;
                Results = viewModels;
            }
            catch (Exception ex)
            {
                // Obsolete Suche: Fehler nicht mehr anzeigen – die neue Suche bestimmt den Status.
                if (coverToken.IsCancellationRequested) return;

                await _errorDialogService.ShowAsync(
                    _localizationService.Get("OnlineSearchFailedTitle"), ex.Message);
            }
            finally
            {
                // Loader nur zurücksetzen, wenn diese Suche noch die aktuelle ist –
                // sonst flackert der Spinner beim Back-to-Back-Wechsel zwischen den Suchen.
                if (!coverToken.IsCancellationRequested)
                {
                    IsLoading = false;
                }

                lock (_inflightSearchesLock)
                {
                    _ = _inflightSearches.Remove(completedSource);
                }
                _ = completedSource.TrySetResult(true);
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
                        Title = series.Title,
                        Source = "Lokal",
                        SourceSeriesId = series.Id.ToString()
                    });
                }
            }

            return localResults;
        }

        /// <summary>
        /// Setzt Suchfeld, Ergebnisliste und Statusanzeigen zurück. Bricht laufende
        /// Cover-Loads ab und macht zugleich eine eventuell noch laufende Suche obsolet,
        /// damit keine HTTP-Requests für die geleerte Trefferliste im Hintergrund
        /// weiterlaufen oder spät eintreffende Treffer in die Liste geschrieben werden.
        ///
        /// Setzt das Such-Feld direkt, ohne den <see cref="SearchText"/>-Setter aufzurufen,
        /// um die Reset-Schleife (Setter ruft Reset bei Leereingabe) zu vermeiden.
        /// </summary>
        private void Reset()
        {
            CancelPendingSearchCovers();

            if (_searchText.Length > 0)
            {
                _searchText = string.Empty;
                OnPropertyChanged(nameof(SearchText));
            }

            ReleaseCurrentResults();
            Results = [];
            _hasSearched = false;
            ShowSuccessHint = false;
            IsSpotifyFallbackHintVisible = false;
            OnPropertyChanged(nameof(EmptyStateVisibility));
        }

        // Pro Treffer ein BitmapImage; ohne Freigabe steigen die Heap-Bytes nach jeder
        // Suche an, weil die Bindings die alten Karten noch kurz halten.
        private void ReleaseCurrentResults()
        {
            foreach (SearchResultViewModel result in _results)
            {
                result.ClearCoverImage();
            }
        }

        /// <summary>
        /// Bricht alle laufenden Cover-Lade-Operationen der aktuellen Trefferliste ab.
        /// Wird beim Verlassen der Page oder vor dem Start einer neuen Suche aufgerufen.
        /// </summary>
        public void CancelPendingSearchCovers()
        {
            CancellationTokenSource? old = _searchCoversCts;
            _searchCoversCts = null;
            if (old is null) return;

            try { old.Cancel(); }
            catch (ObjectDisposedException)
            {
                // CTS war bereits disposed – Cancel ist dann ein No-Op, der weiter unten folgende
                // Dispose-Aufruf ist idempotent. Bewusster Schluck ohne Logging.
            }
            old.Dispose();
        }

        /// <summary>
        /// Beendet den vorherigen Cover-Scope (Cancel + Dispose) und liefert das Token
        /// einer frischen <see cref="CancellationTokenSource"/> für die neue Suche.
        /// </summary>
        private CancellationToken StartNewCoverScope()
        {
            CancelPendingSearchCovers();
            _searchCoversCts = new CancellationTokenSource();
            return _searchCoversCts.Token;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            CancelPendingSearchCovers();
            ReleaseCurrentResults();
        }
    }
}
