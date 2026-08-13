using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Zeigt die lokale Mediathek als dynamisches Akkordeon-Layout.
    /// Serien erscheinen als Cover-Kachelgrid. Nach Auswahl einer Serie klappt der
    /// Folgen-Bereich direkt nach der Reihe der gewählten Kachel auf. Bei Auswahl
    /// einer Folge erscheinen die Tracks in einer Spalte rechts daneben.
    /// Navigation zum Tag-Manager wird über das <see cref="LocalLibraryViewModel.NavigateToTagManagerRequested"/>-Event
    /// ausgelöst – ViewModels navigieren nicht selbst, die Page-Ebene übernimmt das.
    /// Kontextmenü-Handler, Cover- und Fehlende-Folgen-Dialoge liegen in den Partial-Klassen.
    /// </summary>
    public sealed partial class LocalLibraryPage : Page
    {
        // Führt die Buchstaben-Abschnitte ober- und unterhalb des Folgenbereichs und
        // beantwortet die Sprünge der Buchstabenleiste.
        private readonly Helpers.LetterSectionSplitHandler<LocalArtistCardViewModel> _sectionHandler;

        private static readonly EchoPlay.App.Helpers.SafeResourceStrings _resources = new();

        private readonly INavigationService _navigationService;

        /// <summary>Gibt dem XAML-Compiler Zugriff auf das ViewModel für x:Bind.</summary>
        public LocalLibraryViewModel ViewModel { get; }

        /// <summary>
        /// Initialisiert die Seite und bezieht das ViewModel aus dem DI-Container.
        /// </summary>
        public LocalLibraryPage()
        {
            ViewModel = App.Services.GetRequiredService<LocalLibraryViewModel>();
            _navigationService = App.Services.GetRequiredService<INavigationService>();
            InitializeComponent();

            _sectionHandler = new Helpers.LetterSectionSplitHandler<LocalArtistCardViewModel>(
                new Helpers.ItemsControlSectionHost(TopSectionsHost),
                new Helpers.ItemsControlSectionHost(BottomSectionsHost),
                () => ViewModel.ArtistsVM.Artists,
                artist => artist.Title,
                () => ViewModel.ArtistsVM.SelectedArtistIndex,
                () => Math.Max(Helpers.AccordionSplitHelper.SeriesTileSlotWidth, ActualWidth - 16),
                _resources.GetString("LokalLetterSectionFormat"));

            SeriesAlphabetBar.AutomationNameFormat = _resources.GetString("LokalAlphabetJumpFormat");

            InitializeHeaderAndFilters();
        }

        /// <summary>
        /// Beschriftet Seitenkopf, Suchfeld, Filterleiste und den „Nichts gefunden"-Hinweis.
        /// Die Texte stehen im Quelltext und nicht als x:Uid, weil x:Uid nur die Eigenschaften
        /// eingebauter Steuerelemente bedient — die eigenen Bausteine gingen leer aus.
        /// </summary>
        private void InitializeHeaderAndFilters()
        {
            LokalHeader.Title = _resources.GetString("LokalPageTitle");
            LokalHeader.Subtitle = _resources.GetString("LokalPageSubtitle");
            LokalHeader.ActionText = _resources.GetString("LokalScanAction");

            LokalSearchField.PlaceholderText = _resources.GetString("LokalSearchPlaceholder");

            // Der Anfangszustand kommt aus dem ViewModel, nicht aus der Voreinstellung: Die
            // Filter überleben den Seitenwechsel, und ein wirkender Filter ohne aktiven Chip
            // sähe aus wie ein leerer Bestand.
            LokalFilterBar.Chips =
            [
                new Controls.FilterChip(
                    FilterKeyFavorites, _resources.GetString("LokalFilterFavorites"), ViewModel.ArtistsVM.FavoritesOnly),
                new Controls.FilterChip(
                    FilterKeyWatched, _resources.GetString("LokalFilterWatched"), ViewModel.ArtistsVM.WatchedOnly),
                new Controls.FilterChip(
                    FilterKeyIncomplete, _resources.GetString("LokalFilterIncomplete"), ViewModel.ArtistsVM.IncompleteOnly)
            ];

            LokalNoResultsPanel.Message = _resources.GetString("LokalNoResultsMessage");
            LokalNoResultsPanel.ActionText = _resources.GetString("LokalResetFiltersAction");
            LokalNoResultsPanel.ActionCommand = ViewModel.ResetFiltersCommand;
        }

        /// <summary>Schlüssel des Favoriten-Filters.</summary>
        private const string FilterKeyFavorites = "favorites";

        /// <summary>Schlüssel des Filters für überwachte Serien.</summary>
        private const string FilterKeyWatched = "watched";

        /// <summary>Schlüssel des Filters für unvollständige Serien.</summary>
        private const string FilterKeyIncomplete = "incomplete";

        /// <summary>
        /// Übernimmt einen umgeschalteten Filter ins ViewModel. Die Leiste kennt nur ihren
        /// Schlüssel; was er bedeutet, entscheidet die Seite.
        /// </summary>
        private void OnFilterToggled(object? sender, Controls.FilterToggledEventArgs e)
        {
            switch (e.Chip.Key)
            {
                case FilterKeyFavorites:
                    ViewModel.ArtistsVM.FavoritesOnly = e.Chip.IsActive;
                    break;
                case FilterKeyWatched:
                    ViewModel.ArtistsVM.WatchedOnly = e.Chip.IsActive;
                    break;
                case FilterKeyIncomplete:
                    ViewModel.ArtistsVM.IncompleteOnly = e.Chip.IsActive;
                    break;
            }
        }

        /// <summary>
        /// Lädt Bibliothekspfad und Serienliste beim Navigieren zur Seite.
        /// Abonniert außerdem das Tag-Manager-, AddFolder- und PropertyChanged-Event.
        /// </summary>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                // Nur-Online-Modus-Check liegt im ViewModel; bei aktivem Modus navigiert das ViewModel zurück.
                if (!await ViewModel.InitializeAsync())
                {
                    return;
                }

                ViewModel.NavigateToTagManagerRequested += OnNavigateToTagManagerRequested;
                ViewModel.ScanVM.AddFolderRequested += OnAddFolderRequested;
                ViewModel.Actions.MissingEpisodesResolved += OnMissingEpisodesResolved;
                ViewModel.Actions.MissingEpisodesModeRequested += OnMissingEpisodesModeRequested;
                ViewModel.Actions.AllSeriesCheckCompleted += OnAllSeriesCheckCompleted;
                ViewModel.ArtistsVM.PropertyChanged += OnArtistsPropertyChanged;
                SizeChanged += OnPageSizeChanged;
                EpisodeAccordion.GridView.SelectionChanged += OnEpisodeSelectionChanged;
                EpisodeAccordion.GridView.DoubleTapped += OnEpisodeDoubleTapped;
                ViewModel.Activate();
                await ViewModel.Actions.LoadAsync();
            });
        }

        /// <summary>
        /// Deabonniert alle Events beim Verlassen der Seite,
        /// um Memory-Leaks durch hängende Event-Handler zu verhindern.
        /// </summary>
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            ViewModel.NavigateToTagManagerRequested -= OnNavigateToTagManagerRequested;
            ViewModel.ScanVM.AddFolderRequested -= OnAddFolderRequested;
            ViewModel.Actions.MissingEpisodesResolved -= OnMissingEpisodesResolved;
            ViewModel.Actions.MissingEpisodesModeRequested -= OnMissingEpisodesModeRequested;
            ViewModel.Actions.AllSeriesCheckCompleted -= OnAllSeriesCheckCompleted;
            ViewModel.ArtistsVM.PropertyChanged -= OnArtistsPropertyChanged;
            SizeChanged -= OnPageSizeChanged;
            EpisodeAccordion.GridView.SelectionChanged -= OnEpisodeSelectionChanged;
            EpisodeAccordion.GridView.DoubleTapped -= OnEpisodeDoubleTapped;
            ViewModel.Deactivate();
            // VM disposed — Scan-Event-Subscriptions, Sub-VM-Ketten und Koordinatoren freigeben.
            ViewModel.Dispose();
        }

        /// <summary>
        /// Reagiert auf Änderungen an <see cref="LocalArtistsViewModel.Artists"/>
        /// oder <see cref="LocalArtistsViewModel.SelectedArtistIndex"/> und baut die
        /// Buchstaben-Abschnitte neu auf.
        /// </summary>
        private void OnArtistsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(LocalArtistsViewModel.Artists)
                               or nameof(LocalArtistsViewModel.SelectedArtistIndex))
            {
                UpdateSections();
            }
        }

        /// <summary>
        /// Baut die Abschnitte neu und zieht die Buchstabenleiste nach: Was der Bestand nicht
        /// führt, bleibt dort deaktiviert.
        /// </summary>
        private void UpdateSections()
        {
            _sectionHandler.UpdateSections();
            SeriesAlphabetBar.OccupiedLetters = _sectionHandler.OccupiedLetters;
        }

        /// <summary>
        /// Teilt die Abschnitte bei Fenstergrößenänderungen neu auf, damit der Folgenbereich
        /// immer am Ende der richtigen Kachelreihe aufklappt.
        /// </summary>
        private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
        {
            _sectionHandler.HandleSizeChanged();
        }

        /// <summary>
        /// Springt zum Abschnitt des gewählten Buchstabens. Die Leiste hebt ihn danach hervor,
        /// damit erkennbar bleibt, wo man gelandet ist.
        /// </summary>
        private void OnLetterSelected(object? sender, Controls.LetterSelectedEventArgs e)
        {
            if (_sectionHandler.BringLetterIntoView(e.Letter))
            {
                SeriesAlphabetBar.CurrentLetter = e.Letter;
            }
        }

        /// <summary>
        /// Wird ausgelöst, wenn der Nutzer eine Serien-Kachel anklickt. ItemClick statt
        /// SelectionChanged, damit der Re-Klick auf die bereits ausgewählte Kachel
        /// das Akkordeon zuklappt (SelectionChanged feuert bei unveränderter Auswahl nicht).
        /// </summary>
        private async void OnArtistItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is LocalArtistCardViewModel artist)
            {
                EpisodeAccordion.GridView.SelectedItem = null;
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.Actions.SelectArtistAsync(artist));
                // UpdateSplit wird durch PropertyChanged auf SelectedArtistIndex ausgelöst
            }
        }

        /// <summary>
        /// Wird ausgelöst, wenn der Nutzer eine Folgen-Kachel auswählt.
        /// Lädt die Tracks der gewählten Folge.
        /// </summary>
        private async void OnEpisodeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is GridView { SelectedItem: LocalEpisodeCardViewModel episode })
            {
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.Actions.SelectEpisodeAsync(episode));
            }
        }

        /// <summary>
        /// Startet die Folge, auf die der Nutzer doppelt geklickt hat.
        /// Der erste Klick des Doppelklicks hat sie bereits ausgewählt und damit ihre Spuren
        /// geladen — gespielt wird trotzdem über die Folgen-Id, damit der Doppelklick auch
        /// dann greift, wenn die Liste rechts noch lädt.
        /// </summary>
        private async void OnEpisodeDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (sender is GridView { SelectedItem: LocalEpisodeCardViewModel episode })
            {
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.TracksVM.PlayEpisodeAsync(episode));
            }
        }

        /// <summary>
        /// Öffnet den Ordnerpicker und speichert den gewählten Bibliothekspfad.
        /// Das HWND muss manuell aus dem Hauptfenster abgefragt werden, da WinRT-Picker
        /// keinen direkten Window-Zugriff haben.
        /// </summary>
        private async void OnPickFolderClick(object sender, RoutedEventArgs e)
        {
            nint handle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.ScanVM.PickFolderAsync(handle));
        }

        /// <summary>
        /// Öffnet den Ordnerpicker, damit der Nutzer direkt einen Serienordner hinzufügen kann.
        /// </summary>
        private async void OnAddFolderClick(object sender, RoutedEventArgs e)
        {
            nint handle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.ScanVM.AddFolderAsync(handle));
        }

        /// <summary>
        /// Reagiert auf das <see cref="LocalLibraryScanViewModel.AddFolderRequested"/>-Event.
        /// Das ViewModel selbst kennt das HWND nicht – die Page liefert es.
        /// </summary>
        private async void OnAddFolderRequested()
        {
            nint handle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.ScanVM.AddFolderAsync(handle));
        }

        /// <summary>
        /// Schließt den Folgenbereich komplett – setzt die Serienauswahl zurück.
        /// Die Serien-Kacheln füllen wieder die volle Breite, kein Akkordeon sichtbar.
        /// </summary>
        private void OnCloseEpisodePanelClick(object sender, RoutedEventArgs e)
        {
            // Vor dem Zuklappen merken, wohin zurückgekehrt wird: Der Folgenbereich kann weit
            // unten aufgehen, und ohne Rücksprung stünde man danach irgendwo in der Liste statt
            // bei der Serie, die man gerade offen hatte.
            LocalArtistCardViewModel? lastOpened = ViewModel.ArtistsVM.SelectedArtist;

            ViewModel.DeselectArtist();

            if (lastOpened is null)
            {
                return;
            }

            // Erst nach dem Neuaufbau der Abschnitte scrollen — vorher gibt es den Container
            // der Kachel noch nicht, und der Sprung liefe ins Leere.
            _ = DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => _sectionHandler.BringItemIntoView(lastOpened));
        }

        /// <summary>Tab-Wechsel: reguläre Folgen anzeigen.</summary>
        private void OnEpisodeTabRegularChecked(object sender, RoutedEventArgs e)
        {
            ViewModel.EpisodesVM.EpisodeTabIndex = 0;
        }

        /// <summary>Tab-Wechsel: Sonderfolgen anzeigen.</summary>
        private void OnEpisodeTabSpecialChecked(object sender, RoutedEventArgs e)
        {
            ViewModel.EpisodesVM.EpisodeTabIndex = 1;
        }

        /// <summary>
        /// Navigiert zum Tag-Manager mit dem Ordnerpfad als Parameter.
        /// Der Tag-Manager öffnet daraufhin alle Audiodateien des übergebenen Ordners.
        /// </summary>
        private void OnNavigateToTagManagerRequested(string folderPath)
        {
            _navigationService.NavigateTo(NavigationTarget.TagManager, folderPath);
        }

        /// <summary>
        /// Öffnet den Bilddatei-Picker über den gemeinsamen <see cref="Helpers.ImageFilePicker"/>.
        /// </summary>
        private static Task<byte[]?> PickImageFileAsync()
        {
            nint handle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            return Helpers.ImageFilePicker.PickAsync(handle);
        }
    }
}
