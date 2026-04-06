using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;

namespace EchoPlay.App.Pages
{
    /// <summary>
    /// Zwei-Spalten-Ansicht für eine Hörspielserie.
    /// Links: sortierbare Episoden-Kacheln. Rechts: lokale Tracks der gewählten Folge.
    /// Die SeriesId wird beim Navigieren als Parameter übergeben.
    /// </summary>
    public sealed partial class SeriesDetailPage : Page
    {
        /// <summary>Gibt dem XAML-Compiler Zugriff auf das ViewModel für x:Bind.</summary>
        public SeriesDetailViewModel ViewModel { get; }

        /// <summary>
        /// Initialisiert die Seite und bezieht das ViewModel aus dem DI-Container.
        /// </summary>
        public SeriesDetailPage()
        {
            ViewModel = App.Services.GetRequiredService<SeriesDetailViewModel>();
            InitializeComponent();

            // Sortierung initial auf Episodennummer (Index 0) vorauswählen
            SortComboBox.SelectedIndex = 0;
        }

        /// <summary>
        /// Wird beim Navigieren zur Seite aufgerufen.
        /// Erwartet eine <see cref="Guid"/> als Navigationsparameter (SeriesId).
        /// </summary>
        /// <param name="e">Enthält die SeriesId als <see cref="NavigationEventArgs.Parameter"/>.</param>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is Guid seriesId)
            {
                await ViewModel.LoadAsync(seriesId);
            }
        }

        /// <summary>
        /// Wird ausgelöst, wenn der Benutzer eine Episodenkachel auswählt.
        /// Lädt die zugehörigen lokalen Tracks in die rechte Spalte.
        /// </summary>
        /// <summary>Tab-Wechsel: reguläre Folgen.</summary>
        private void OnDetailTabRegularChecked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel.EpisodeTabIndex = 0;
        }

        /// <summary>Tab-Wechsel: Sonderfolgen.</summary>
        private void OnDetailTabSpecialChecked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            ViewModel.EpisodeTabIndex = 1;
        }

        /// <summary>
        /// Navigiert zur vorherigen Seite (z.B. Dashboard oder Mediathek).
        /// </summary>
        private void OnBackClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        /// <param name="sender">Das GridView mit den Episodenkacheln.</param>
        /// <param name="e">Enthält die neue Auswahl.</param>
        private async void OnEpisodeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is GridView { SelectedItem: EpisodeTileViewModel episode })
            {
                await ViewModel.SelectEpisodeAsync(episode);
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
            if (sender is ComboBox { SelectedIndex: >= 0 and var index })
            {
                ViewModel.SortOrder = (EpisodeSortOrder)index;
            }
        }

        /// <summary>
        /// Startet die Wiedergabe aller Tracks der aktuell gewählten Folge.
        /// </summary>
        /// <param name="sender">Der "Ganze Folge abspielen"-Button.</param>
        /// <param name="e">Ereignisargumente.</param>
        private async void OnPlayEpisodeClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            await ViewModel.PlaySelectedEpisodeAsync();
        }

        /// <summary>
        /// Markiert eine Episode als gehört über das Kontextmenü.
        /// </summary>
        private async void OnEpisodeMarkPlayedClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { Tag: Guid episodeId })
            {
                await ViewModel.MarkAsPlayedAsync(episodeId);
            }
        }

        /// <summary>
        /// Markiert eine Episode als ungehört über das Kontextmenü.
        /// </summary>
        private async void OnEpisodeMarkUnplayedClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { Tag: Guid episodeId })
            {
                await ViewModel.MarkAsUnplayedAsync(episodeId);
            }
        }
    }
}
