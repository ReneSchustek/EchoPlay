using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.ComponentModel;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Online-Mediathek mit Akkordeon-Layout.
    /// Serien als Cover-Kachelgrid, Folgen klappen unterhalb der gewählten Reihe auf.
    /// Unterstützt Inline-Provider-Suche zum Hinzufügen neuer Serien.
    /// </summary>
    public sealed partial class OnlineLibraryPage : Page
    {
        /// <summary>
        /// Führt die Abschnitte ober- und unterhalb des Folgenbereichs und beantwortet die
        /// Sprünge der Buchstabenleiste.
        /// </summary>
        private readonly Helpers.LetterSectionSplitHandler<SeriesCardViewModel> _sectionHandler;

        /// <summary>Sortierindex, bei dem die Bibliothek nach Namen geordnet ist.</summary>
        private const int SortByNameIndex = 0;

        private static readonly Helpers.SafeResourceStrings _resources = new();

        private readonly INavigationService _navigationService;

        /// <summary>Gibt dem XAML-Compiler Zugriff auf das ViewModel für x:Bind.</summary>
        public OnlineLibraryViewModel ViewModel { get; }

        /// <summary>
        /// Initialisiert die Seite und bezieht das ViewModel aus dem DI-Container.
        /// </summary>
        public OnlineLibraryPage()
        {
            ViewModel = App.Services.GetRequiredService<OnlineLibraryViewModel>();
            _navigationService = App.Services.GetRequiredService<INavigationService>();
            InitializeComponent();

            _sectionHandler = new Helpers.LetterSectionSplitHandler<SeriesCardViewModel>(
                new Helpers.ItemsControlSectionHost(TopSectionsHost),
                new Helpers.ItemsControlSectionHost(BottomSectionsHost),
                new Helpers.LetterSectionSource<SeriesCardViewModel>(
                    () => ViewModel.Series,
                    card => card.Title,
                    () => ViewModel.SelectedSeriesIndex,
                    () => Math.Max(Helpers.AccordionSplitHelper.SeriesTileSlotWidth, ActualWidth - 32)),
                _resources.GetString("OnlineLetterSectionFormat"),
                new Helpers.LetterSectionGrouping(
                    () => ViewModel.SeriesSortIndex == SortByNameIndex,
                    _resources.GetString("OnlineSeriesListName")));

            SeriesAlphabetBar.AutomationNameFormat = _resources.GetString("OnlineAlphabetJumpFormat");

            InitializeHeaderAndFilters();
        }

        /// <summary>Schlüssel des Filters für Serien mit neuen Folgen.</summary>
        private const string FilterKeyNew = "new";

        /// <summary>Schlüssel des Filters für angefangene Serien.</summary>
        private const string FilterKeyInProgress = "inprogress";

        /// <summary>Schlüssel des Filters für durchgehörte Serien.</summary>
        private const string FilterKeyFinished = "finished";

        /// <summary>Die Chips des Statusfilters, gehalten, weil sie einander ausschließen.</summary>
        private Controls.FilterChip[] _statusChips = [];

        /// <summary>
        /// Beschriftet Seitenkopf, Suchfeld und Filterleiste. Die Texte stehen im Quelltext und
        /// nicht als x:Uid, weil x:Uid nur die Eigenschaften eingebauter Steuerelemente bedient.
        /// </summary>
        private void InitializeHeaderAndFilters()
        {
            OnlineHeader.Title = _resources.GetString("OnlinePageTitle");
            OnlineHeader.Subtitle = _resources.GetString("OnlinePageSubtitle");
            OnlineHeader.ActionText = _resources.GetString("OnlineRefreshAction");

            SearchBox.PlaceholderText = _resources.GetString("OnlineSearchPlaceholder");

            _statusChips =
            [
                new Controls.FilterChip(FilterKeyNew, _resources.GetString("OnlineFilterNew")),
                new Controls.FilterChip(FilterKeyInProgress, _resources.GetString("OnlineFilterInProgress")),
                new Controls.FilterChip(FilterKeyFinished, _resources.GetString("OnlineFilterFinished"))
            ];

            OnlineFilterBar.Chips = _statusChips;
        }

        /// <summary>
        /// Lädt die Serienliste und registriert Events. Der Offline-Modus-Check
        /// liegt im ViewModel; bei aktivem Offline-Modus navigiert das ViewModel
        /// selbstständig zurück.
        /// </summary>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            await AsyncEventHandler.RunSafelyAsync(async () =>
            {
                if (!await ViewModel.InitializeAsync())
                {
                    return;
                }

                ViewModel.PropertyChanged += OnViewModelPropertyChanged;
                ViewModel.FocusSearchRequested += OnFocusSearchRequested;
                SizeChanged += OnPageSizeChanged;

                await ViewModel.LoadAsync();
            });
        }

        /// <summary>
        /// Deabonniert Events beim Verlassen der Seite.
        /// </summary>
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.FocusSearchRequested -= OnFocusSearchRequested;
            SizeChanged -= OnPageSizeChanged;
            ViewModel.Dispose();
        }

        /// <summary>
        /// Setzt den Fokus auf die Suchbox; wird vom <see cref="OnlineLibraryViewModel.FocusSearchRequested"/>-Event
        /// ausgelöst, wenn der Empty-State-Button "Serie suchen" geklickt wurde.
        /// </summary>
        private void OnFocusSearchRequested()
        {
            _ = SearchBox.Focus(FocusState.Programmatic);
        }

        /// <summary>
        /// Reagiert auf Änderungen der Serienliste, des Auswahl-Index oder der Sortierung und
        /// baut die Abschnitte neu auf. Die Sortierung zählt mit, weil sie darüber entscheidet,
        /// ob die Liste überhaupt nach Buchstaben gegliedert wird.
        /// </summary>
        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(OnlineLibraryViewModel.Series)
                               or nameof(OnlineLibraryViewModel.SelectedSeriesIndex)
                               or nameof(OnlineLibraryViewModel.SeriesSortIndex)
                               or nameof(OnlineLibraryViewModel.LibraryVisibility))
            {
                UpdateSections();
            }
        }

        /// <summary>
        /// Baut die Abschnitte neu und richtet die Buchstabenleiste danach aus. Die Leiste
        /// verschwindet, sobald die Liste nicht mehr nach Namen sortiert ist oder die Seite
        /// Suchergebnisse statt der Bibliothek zeigt, sonst zeigte sie auf Sprungziele, die
        /// es gerade nicht gibt.
        /// </summary>
        private void UpdateSections()
        {
            _sectionHandler.UpdateSections();
            SeriesAlphabetBar.OccupiedLetters = _sectionHandler.OccupiedLetters;

            bool barIsUseful = _sectionHandler.IsGroupedByLetter
                && ViewModel.LibraryVisibility == Visibility.Visible;

            SeriesAlphabetBar.Visibility = barIsUseful ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Springt zum Abschnitt des gewählten Buchstabens und hebt ihn in der Leiste hervor.
        /// </summary>
        private void OnLetterSelected(object? sender, Controls.LetterSelectedEventArgs e)
        {
            if (_sectionHandler.BringLetterIntoView(e.Letter))
            {
                SeriesAlphabetBar.CurrentLetter = e.Letter;
            }
        }

        /// <summary>
        /// Bei Fenstergrößenänderung die Aufteilung neu berechnen.
        /// </summary>
        private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
        {
            _sectionHandler.HandleSizeChanged();
        }

        /// <summary>
        /// Wird ausgelöst wenn der Nutzer eine Serien-Kachel klickt.
        /// Lädt die Episoden und öffnet das Akkordeon.
        /// </summary>
        private async void OnSeriesItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SeriesCardViewModel card)
            {
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.SelectSeriesAsync(card));
            }
        }

        /// <summary>
        /// Schließt das Akkordeon.
        /// </summary>
        private void OnCloseEpisodePanelClick(object sender, RoutedEventArgs e)
        {
            // Vor dem Zuklappen merken, wohin zurückgekehrt wird: Der Folgenbereich kann weit
            // unten aufgehen, und ohne Rücksprung stünde man danach irgendwo in der Liste statt
            // bei der Serie, die man gerade offen hatte.
            int index = ViewModel.SelectedSeriesIndex;
            SeriesCardViewModel? lastOpened =
                index >= 0 && index < ViewModel.Series.Count ? ViewModel.Series[index] : null;

            ViewModel.DeselectSeries();

            if (lastOpened is null)
            {
                return;
            }

            // Erst nach dem Neuaufbau der Abschnitte scrollen, denn vorher gibt es den Container
            // der Kachel noch nicht, und der Sprung liefe ins Leere.
            _ = DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => _sectionHandler.BringItemIntoView(lastOpened));
        }


        /// <summary>
        /// Übernimmt einen umgeschalteten Statusfilter. Die Zustände schließen einander aus,
        /// deshalb geht mit dem neuen Chip jeder andere aus, und ein zweiter Klick auf den
        /// aktiven Chip führt zurück zu „alle".
        /// </summary>
        private void OnFilterToggled(object? sender, Controls.FilterToggledEventArgs e)
        {
            foreach (Controls.FilterChip chip in _statusChips)
            {
                if (!ReferenceEquals(chip, e.Chip))
                {
                    chip.IsActive = false;
                }
            }

            ViewModel.StatusFilter = e.Chip.IsActive
                ? e.Chip.Key switch
                {
                    FilterKeyNew => SeriesStatusFilter.New,
                    FilterKeyInProgress => SeriesStatusFilter.InProgress,
                    FilterKeyFinished => SeriesStatusFilter.Finished,
                    _ => SeriesStatusFilter.All
                }
                : SeriesStatusFilter.All;
        }

        /// <summary>
        /// Sucht beim Anbieter weiter, wenn im Suchfeld die Eingabetaste gedrückt wurde. Das
        /// Filtern der Bibliothek geschieht schon beim Tippen und braucht das nicht.
        /// </summary>
        private void OnSearchSubmitted(object? sender, Controls.SearchSubmittedEventArgs e)
        {
            ViewModel.SearchText = e.Query;

            if (ViewModel.ProviderSearchCommand.CanExecute(null))
            {
                ViewModel.ProviderSearchCommand.Execute(null);
            }
        }

    }
}
