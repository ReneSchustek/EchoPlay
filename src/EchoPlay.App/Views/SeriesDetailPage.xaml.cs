using EchoPlay.App.Infrastructure;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Zwei-Spalten-Ansicht für eine Hörspielserie.
    /// Links: sortierbare Episoden-Kacheln. Rechts: lokale Tracks der gewählten Folge.
    /// Die SeriesId wird beim Navigieren als Parameter übergeben.
    /// </summary>
    public sealed partial class SeriesDetailPage : Page
    {
        private readonly INavigationService _navigationService;

        /// <summary>Gibt dem XAML-Compiler Zugriff auf das ViewModel für x:Bind.</summary>
        public SeriesDetailViewModel ViewModel { get; }

        /// <summary>
        /// Initialisiert die Seite und bezieht das ViewModel aus dem DI-Container.
        /// </summary>
        public SeriesDetailPage()
        {
            ViewModel = App.Services.GetRequiredService<SeriesDetailViewModel>();
            _navigationService = App.Services.GetRequiredService<INavigationService>();
            InitializeComponent();

            // Sortierung initial auf Episodennummer (Index 0) vorauswählen
            SortComboBox.SelectedIndex = 0;

            InitializeSearchAndFilters();
        }

        private static readonly Helpers.SafeResourceStrings _resources = new();

        /// <summary>Schlüssel des Filters für noch nicht begonnene Folgen.</summary>
        private const string FilterKeyUnheard = "unheard";

        /// <summary>Schlüssel des Filters für angefangene Folgen.</summary>
        private const string FilterKeyInProgress = "inprogress";

        /// <summary>Schlüssel des Filters für durchgehörte Folgen.</summary>
        private const string FilterKeyHeard = "heard";

        /// <summary>Die Chips des Hörzustands — gehalten, weil sie einander ausschließen.</summary>
        private Controls.FilterChip[] _stateChips = [];

        /// <summary>
        /// Beschriftet Suchfeld und Filterleiste. Die Texte stehen im Quelltext und nicht als
        /// x:Uid, weil x:Uid nur die Eigenschaften eingebauter Steuerelemente bedient.
        /// </summary>
        private void InitializeSearchAndFilters()
        {
            EpisodeSearchField.PlaceholderText = _resources.GetString("SeriesDetailSearchPlaceholder");

            _stateChips =
            [
                new Controls.FilterChip(FilterKeyUnheard, _resources.GetString("SeriesDetailFilterUnheard")),
                new Controls.FilterChip(FilterKeyInProgress, _resources.GetString("SeriesDetailFilterInProgress")),
                new Controls.FilterChip(FilterKeyHeard, _resources.GetString("SeriesDetailFilterHeard"))
            ];

            EpisodeFilterBar.Chips = _stateChips;
        }

        /// <summary>
        /// Übernimmt einen umgeschalteten Hörzustand. Die Zustände schließen einander aus,
        /// deshalb geht mit dem neuen Chip jeder andere aus; ein zweiter Klick auf den aktiven
        /// Chip führt zurück zu „alle".
        /// </summary>
        private void OnFilterToggled(object? sender, Controls.FilterToggledEventArgs e)
        {
            foreach (Controls.FilterChip chip in _stateChips)
            {
                if (!ReferenceEquals(chip, e.Chip))
                {
                    chip.IsActive = false;
                }
            }

            // Die Indizes entsprechen der früheren Aufklappliste: 1 = ungehört,
            // 2 = gehört, 3 = angefangen.
            ViewModel.EpisodeList.EpisodeFilterIndex = e.Chip.IsActive
                ? e.Chip.Key switch
                {
                    FilterKeyUnheard => 1,
                    FilterKeyHeard => 2,
                    FilterKeyInProgress => 3,
                    _ => 0
                }
                : 0;
        }

        /// <summary>
        /// Wird beim Navigieren zur Seite aufgerufen.
        /// Erwartet eine <see cref="Guid"/> als Navigationsparameter (SeriesId).
        /// </summary>
        /// <param name="e">Enthält die SeriesId als <see cref="NavigationEventArgs.Parameter"/>.</param>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            ArgumentNullException.ThrowIfNull(e);
            base.OnNavigatedTo(e);

            if (e.Parameter is Guid seriesId)
            {
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.LoadAsync(seriesId));
            }
        }

        /// <summary>
        /// Wird beim Verlassen der Seite aufgerufen. Bricht den Priority-Cover-Load
        /// der offenen Serie ab, damit der Hintergrund-Cover-Loop nicht unnötig
        /// für eine nicht mehr sichtbare Serie pausiert bleibt.
        /// </summary>
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            // Cleanup() bricht zusätzlich den lifecycle-CTS ab, sodass laufende
            // DB-Roundtrips beim Page-Verlassen nicht mehr in den verworfenen VM-State schreiben.
            ViewModel.Cleanup();
        }

        /// <summary>
        /// Wird ausgelöst, wenn der Benutzer eine Episodenkachel auswählt.
        /// Lädt die zugehörigen lokalen Tracks in die rechte Spalte.
        /// </summary>
        /// <summary>Tab-Wechsel: reguläre Folgen.</summary>
        private void OnDetailTabRegularChecked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel.EpisodeList.EpisodeTabIndex = 0;
        }

        /// <summary>Tab-Wechsel: Sonderfolgen.</summary>
        private void OnDetailTabSpecialChecked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel.EpisodeList.EpisodeTabIndex = 1;
        }

        /// <summary>
        /// Navigiert zur vorherigen Seite (z.B. Dashboard oder Mediathek).
        /// </summary>
        private void OnBackClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _ = _navigationService.GoBack();
        }

        /// <param name="sender">Das GridView mit den Episodenkacheln.</param>
        /// <param name="e">Enthält die neue Auswahl.</param>
        private async void OnEpisodeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is GridView { SelectedItem: EpisodeTileViewModel episode })
            {
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.TrackList.SelectEpisodeAsync(episode));
            }
        }

        /// <summary>
        /// Wird ausgelöst, wenn der Benutzer die Sortier-ComboBox ändert.
        /// Der SelectedIndex entspricht direkt dem <see cref="EpisodeSortOrder"/>-Enum-Wert.
        /// </summary>
        /// <param name="sender">Die Sortier-ComboBox.</param>
        /// <param name="e">Enthält die neue Auswahl.</param>
        private void OnSortOrderChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox { SelectedIndex: >= 0 and int index })
            {
                ViewModel.EpisodeList.SortOrder = (EpisodeSortOrder)index;
            }
        }

        /// <summary>
        /// Startet die Wiedergabe aller Tracks der aktuell gewählten Folge.
        /// </summary>
        /// <param name="sender">Der "Ganze Folge abspielen"-Button.</param>
        /// <param name="e">Ereignisargumente.</param>
        private async void OnPlayEpisodeClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            await AsyncEventHandler.RunSafelyAsync(() => ViewModel.PlaySelectedEpisodeAsync());
        }

        /// <summary>
        /// Markiert eine Episode als gehört über das Kontextmenü.
        /// </summary>
        private async void OnEpisodeMarkPlayedClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { Tag: Guid episodeId })
            {
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.MarkAsPlayedAsync(episodeId));
            }
        }

        /// <summary>
        /// Markiert eine Episode als ungehört über das Kontextmenü.
        /// </summary>
        private async void OnEpisodeMarkUnplayedClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { Tag: Guid episodeId })
            {
                await AsyncEventHandler.RunSafelyAsync(() => ViewModel.MarkAsUnplayedAsync(episodeId));
            }
        }

        /// <summary>
        /// Öffnet das Album der Folge in Spotify. Die Wiedergabe startet dort der Nutzer selbst –
        /// EchoPlay bekommt von Spotify keinen Fortschritt zurück.
        /// </summary>
        private void OnOpenInSpotifyClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { DataContext: EchoPlay.App.ViewModels.EpisodeTileViewModel tile })
            {
                _ = tile.OpenInSpotify();
            }
        }

        /// <summary>
        /// Öffnet das Album der Folge in Apple Music. Wie bei Spotify startet die Wiedergabe
        /// dort der Nutzer selbst.
        /// </summary>
        private void OnOpenInAppleMusicClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { DataContext: EchoPlay.App.ViewModels.EpisodeTileViewModel tile })
            {
                _ = tile.OpenInAppleMusic();
            }
        }
    }
}
