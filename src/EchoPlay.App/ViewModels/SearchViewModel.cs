using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.Core.Models;
using EchoPlay.Core.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
    public sealed class SearchViewModel : ObservableObject, IDisposable
    {
        private readonly IErrorDialogService _errorDialogService;
        private readonly ILocalizationService _localizationService;

        // Die Durchführung einer Suche: Anbieter fragen, Bestand durchsuchen, Treffer bauen.
        // Das Ansichtsmodell hält Eingabe, Zustand und Liste.
        private readonly SearchRunner _searchRunner;

        // Zentraler Navigationsdienst, in Unit-Tests optional, damit der Ctor testbar bleibt
        private readonly INavigationService? _navigationService;

        // Page-Mode-Guard prüft den Offline-Modus beim Betreten der Page; null in Tests
        private readonly IPageModeGuard? _pageModeGuard;

        private readonly SearchTimingLog _timing;
        private readonly SearchResultList _results = new();
        private readonly SearchRunTracker _runs = new();

        private string _searchText = string.Empty;
        private bool _isLoading;
        private bool _hasSearched;
        private bool _isOnboardingHintVisible;
        private bool _showSuccessHint;
        private bool _isSpotifyFallbackHintVisible;
        private int _selectedScopeIndex;

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
        /// Optionaler Page-Mode-Guard; prüft den Offline-Modus beim Betreten der Page.
        /// In Tests kann der Parameter weggelassen werden, dann wird der Check übersprungen.
        /// </param>
        /// <param name="backgroundCoverService">
        /// Optionale zentrale Cover-Pipeline. Wird an <see cref="SearchResultViewModel"/>
        /// weitergereicht; bei <see langword="null"/> bleiben die Trefferkacheln ohne Cover.
        /// </param>
        /// <param name="logger">
        /// Optionaler Protokollkanal für die Laufzeitmarken einer Suche. In Tests kann der
        /// Parameter weggelassen werden.
        /// </param>
        public SearchViewModel(
            ImportService importService,
            IErrorDialogService errorDialogService,
            ILocalizationService localizationService,
            IServiceScopeFactory? scopeFactory = null,
            INavigationService? navigationService = null,
            IPageModeGuard? pageModeGuard = null,
            BackgroundCoverService? backgroundCoverService = null,
            EchoPlay.Logger.Abstractions.ILogger? logger = null)
        {
            _errorDialogService = errorDialogService;
            _localizationService = localizationService;
            _navigationService = navigationService;
            _pageModeGuard = pageModeGuard;
            _timing = new SearchTimingLog(logger);

            _results.Items.CollectionChanged += OnResultsChanged;

            _searchRunner = new SearchRunner(
                importService, errorDialogService, localizationService,
                scopeFactory, backgroundCoverService, this);

            SearchCommand = new RelayCommand(() => _ = SearchAsync());
            ResetCommand = new RelayCommand(Reset);
        }

        /// <summary>
        /// Wird beim Betreten der Seite aufgerufen. Prüft den Offline-Modus, verarbeitet den
        /// Navigationsparameter (<c>"onboarding"</c> oder ein Suchtext) und löst ggf. eine Suche aus.
        /// Bei aktivem Offline-Modus wird ein Hinweis gezeigt und zurück zur vorherigen Seite navigiert.
        /// </summary>
        /// <param name="parameter">Navigationsparameter der Page, meist ein String oder <see langword="null"/>.</param>
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
            _navigationService?.NavigateTo(NavigationTarget.OnlineLibrary);
        }

        /// <summary>
        /// Steuert den Onboarding-Hinweis, der beim allerersten Start erscheint.
        /// Wird auf <see langword="true"/> gesetzt, wenn die Seite mit dem Parameter "onboarding"
        /// aufgerufen wird, also wenn noch keine Serien in der Datenbank vorhanden sind.
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
        /// Sucheingabe des Nutzers. Wird im TwoWay-Binding mit dem Suchfeld verknüpft.
        /// Beim Leerwerden (Löschen-Zeichen im Feld oder vollständiges Löschen per Tastatur)
        /// löst der Setter automatisch <see cref="Reset"/> aus, damit Treffer und
        /// Status-Hinweise sofort verschwinden.
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

        /// <summary>
        /// Suchergebnisse der laufenden oder zuletzt beendeten Anfrage. Die Liste wächst
        /// während der Suche, jede Karte erscheint, sobald ihr Treffer feststeht.
        /// </summary>
        public ObservableCollection<SearchResultViewModel> Results => _results.Items;

        private void OnResultsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(EmptyStateVisibility));
        }

        /// <summary>Reiht eine Anbieter-Karte an ihrem Rang ein (siehe <see cref="SearchResultList.InsertOnline"/>).</summary>
        /// <param name="card">Die fertige Trefferkarte.</param>
        /// <param name="rank">Ihr Rang zum Suchbegriff.</param>
        internal void PublishOnlineResult(SearchResultViewModel card, SearchResultRank rank)
        {
            _results.InsertOnline(card, rank);
            _timing.ResultShown();
        }

        /// <summary>Hängt eine Karte aus dem eigenen Bestand hinten an.</summary>
        /// <param name="card">Die fertige Trefferkarte.</param>
        internal void PublishLocalResult(SearchResultViewModel card)
        {
            _results.AddLocal(card);
            _timing.ResultShown();
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
            _hasSearched && !_isLoading && _results.Items.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        /// <summary>Wartet auf alle gerade laufenden Suchen (für deterministische Tests).</summary>
        internal Task WaitForSearchCompleteAsync() => _runs.WaitForAllAsync();

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
        /// der HTTP-Call startet. Das Token aus <see cref="SearchRunTracker.Begin"/> markiert
        /// zugleich obsolete Suchen. Nach jedem Await prüft der Code, ob inzwischen eine
        /// neue Suche begonnen hat und verwirft dann die eigenen Ergebnisse.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Such-Command der Suche-Seite: Provider-HTTP-/Parser-/Timeout-Fehler werden als Nutzer-Status gespiegelt, damit der Command nicht reißt.")]
        private async Task SearchAsync()
        {
            if (string.IsNullOrWhiteSpace(_searchText))
            {
                return;
            }

            // Die aktuelle Suche wird Inhaber des Tokens, alle älteren sehen ab hier
            // IsCancellationRequested == true und verwerfen ihre Treffer.
            (CancellationToken coverToken, TaskCompletionSource<bool> completedSource) = _runs.Begin();

            // Sucheingabe einfrieren, damit nachträgliche Änderungen am Feld während
            // der laufenden Anfrage den Treffer-Build nicht verfälschen.
            string searchText = _searchText;
            SearchSource scope = SelectedScope;

            ResetResultsForNewSearch();
            _timing.Started(searchText);

            try
            {
                SearchRunner.SearchRunResult? outcome = await _searchRunner.RunAsync(searchText, scope, coverToken);
                if (outcome is null) return;

                await ShowOutcomeAsync(outcome, coverToken);
            }
            catch (Exception ex)
            {
                await ShowFailureUnlessObsoleteAsync(ex, isObsolete: coverToken.IsCancellationRequested);
            }
            finally
            {
                // Loader nur zurücksetzen, wenn diese Suche noch die aktuelle ist,
                // sonst flackert der Spinner beim Back-to-Back-Wechsel zwischen den Suchen.
                if (!coverToken.IsCancellationRequested)
                {
                    IsLoading = false;
                }

                _runs.End(completedSource);
            }
        }

        /// <summary>
        /// Zeigt einen Restfehler (z.B. lokale DB); eine überholte Suche zeigt keinen mehr.
        /// </summary>
        private Task ShowFailureUnlessObsoleteAsync(Exception ex, bool isObsolete) =>
            isObsolete
                ? Task.CompletedTask
                : _errorDialogService.ShowAsync(
                    _localizationService.Get("OnlineSearchFailedTitle"), ex.Message, DialogKey.OnlineSearchFailed);

        /// <summary>
        /// Leert die Trefferansicht VOR dem ersten Await: Die UI zeigt sofort Loader und leere
        /// Liste, alte Karten verschwinden noch im selben Frame.
        /// </summary>
        private void ResetResultsForNewSearch()
        {
            _hasSearched = false;
            _results.Clear();
            ShowSuccessHint = false;
            IsOnboardingHintVisible = false;
            IsSpotifyFallbackHintVisible = false;
            IsLoading = true;
        }

        /// <summary>
        /// Zeigt das Ergebnis einer Suche. Der Online-Fehlerhinweis kommt erst NACH den lokalen
        /// Treffern, denn die lokale Serie ist dann bereits sichtbar, unabhängig vom Ausgang des
        /// Online-Zweigs.
        /// </summary>
        private async Task ShowOutcomeAsync(SearchRunner.SearchRunResult outcome, CancellationToken coverToken)
        {
            IsSpotifyFallbackHintVisible = outcome.SpotifyFallbackApplied;
            _hasSearched = true;
            OnPropertyChanged(nameof(EmptyStateVisibility));

            _timing.Completed(_results.Items.Count);

            // Das Token sagt nur, ob die Suche noch aktuell ist; den offenen Dialog bricht es nicht ab.
            if (outcome.OnlineError is not null && !coverToken.IsCancellationRequested)
            {
                await _errorDialogService.ShowAsync(
                    _localizationService.Get("OnlineSearchFailedTitle"), outcome.OnlineError.Message, DialogKey.OnlineSearchFailed, CancellationToken.None);
            }
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

            _results.Clear();
            _hasSearched = false;
            ShowSuccessHint = false;
            IsSpotifyFallbackHintVisible = false;
            OnPropertyChanged(nameof(EmptyStateVisibility));
        }

        /// <summary>
        /// Bricht alle laufenden Cover-Lade-Operationen der aktuellen Trefferliste ab.
        /// Wird beim Verlassen der Page aufgerufen.
        /// </summary>
        public void CancelPendingSearchCovers() => _runs.CancelCurrent();

        /// <inheritdoc/>
        public void Dispose()
        {
            _runs.Dispose();
            _results.ReleaseCovers();
        }
    }
}
