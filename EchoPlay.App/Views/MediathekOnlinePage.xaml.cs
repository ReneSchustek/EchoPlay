using EchoPlay.App.Helpers;
using EchoPlay.App.Models;
using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.ComponentModel;
using System.Linq;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Online-Mediathek mit Akkordeon-Layout.
    /// Serien als Cover-Kachelgrid, Folgen klappen unterhalb der gewählten Reihe auf.
    /// Unterstützt Inline-Provider-Suche zum Hinzufügen neuer Serien.
    /// </summary>
    public sealed partial class MediathekOnlinePage : Page
    {
        /// <summary>Breite eines Serien-Kachel-Slots (140px Kachel + je 4px Margin = 148px).</summary>

        /// <summary>Verhindert Endlos-Rekursion bei SelectionChanged während des Grid-Splits.</summary>
        private bool _isUpdatingSplit;

        /// <summary>Letzte berechnete Kacheln pro Reihe – Debounce für SizeChanged.</summary>
        private int _lastTilesPerRow;

        private readonly INavigationService _navigationService;

        /// <summary>Gibt dem XAML-Compiler Zugriff auf das ViewModel für x:Bind.</summary>
        public MediathekOnlineViewModel ViewModel { get; }

        /// <summary>
        /// Initialisiert die Seite und bezieht das ViewModel aus dem DI-Container.
        /// </summary>
        public MediathekOnlinePage()
        {
            ViewModel          = App.Services.GetRequiredService<MediathekOnlineViewModel>();
            _navigationService = App.Services.GetRequiredService<INavigationService>();
            InitializeComponent();
        }

        /// <summary>
        /// Lädt die Serienliste und registriert Events. Der Offline-Modus-Check
        /// liegt im ViewModel; bei aktivem Offline-Modus navigiert das ViewModel
        /// selbstständig zurück.
        /// </summary>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (!await ViewModel.InitializeAsync())
            {
                return;
            }

            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            SizeChanged               += OnPageSizeChanged;

            await ViewModel.LoadAsync();
        }

        /// <summary>
        /// Deabonniert Events beim Verlassen der Seite.
        /// </summary>
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            SizeChanged               -= OnPageSizeChanged;
        }

        // ── Akkordeon Split-Logik ────────────────────────────────────────────────

        /// <summary>
        /// Berechnet die Aufteilung der Serien auf Top- und Bottom-Grid,
        /// damit das Akkordeon am Ende der Reihe der gewählten Serie erscheint.
        /// </summary>
        private void UpdateSeriesSplit()
        {
            _isUpdatingSplit = true;

            try
            {
                System.Collections.Generic.IReadOnlyList<SeriesCardViewModel> all = ViewModel.Series;
                int selectedIndex = ViewModel.SelectedSeriesIndex;

                if (selectedIndex < 0 || all.Count == 0)
                {
                    SeriesTopGrid.ItemsSource    = all;
                    SeriesBottomGrid.ItemsSource = System.Array.Empty<SeriesCardViewModel>();
                    return;
                }

                double availableWidth = ActualWidth > 0 ? ActualWidth - 32 : 800;
                int splitIndex = AccordionSplitHelper.CalculateSplitIndex(
                    selectedIndex, all.Count, availableWidth);

                (System.Collections.Generic.IReadOnlyList<SeriesCardViewModel> top,
                 System.Collections.Generic.IReadOnlyList<SeriesCardViewModel> bottom) =
                    AccordionSplitHelper.Split(all, splitIndex);

                SeriesTopGrid.ItemsSource    = top;
                SeriesBottomGrid.ItemsSource = bottom;

                if (selectedIndex < splitIndex)
                {
                    SeriesTopGrid.SelectedIndex = selectedIndex;
                }
            }
            finally
            {
                _isUpdatingSplit = false;
            }
        }

        /// <summary>
        /// Reagiert auf Änderungen der Serienliste oder des Auswahl-Index.
        /// </summary>
        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MediathekOnlineViewModel.Series)
                               or nameof(MediathekOnlineViewModel.SelectedSeriesIndex))
            {
                UpdateSeriesSplit();
            }
        }

        /// <summary>
        /// Bei Fenstergrößenänderung den Split neu berechnen.
        /// </summary>
        private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
        {
            double availableWidth = ActualWidth > 0 ? ActualWidth - 32 : 800;
            int tilesPerRow = AccordionSplitHelper.CalculateTilesPerRow(availableWidth);

            if (tilesPerRow != _lastTilesPerRow)
            {
                _lastTilesPerRow = tilesPerRow;
                UpdateSeriesSplit();
            }
        }

        // ── Event-Handler ────────────────────────────────────────────────────────

        /// <summary>
        /// Wird ausgelöst wenn der Nutzer eine Serien-Kachel klickt.
        /// Lädt die Episoden und öffnet das Akkordeon.
        /// </summary>
        private async void OnSeriesItemClick(object sender, ItemClickEventArgs e)
        {
            if (_isUpdatingSplit) return;

            if (e.ClickedItem is SeriesCardViewModel card)
            {
                await ViewModel.SelectSeriesAsync(card);
            }
        }

        /// <summary>
        /// Schließt das Akkordeon.
        /// </summary>
        private void OnCloseEpisodePanelClick(object sender, RoutedEventArgs e)
        {
            ViewModel.DeselectSeries();
        }


        /// <summary>
        /// Statusfilter-Änderung.
        /// </summary>
        private void OnStatusFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                ViewModel.StatusFilter = comboBox.SelectedIndex switch
                {
                    1 => SeriesStatusFilter.Neu,
                    2 => SeriesStatusFilter.AmHoeren,
                    3 => SeriesStatusFilter.Gehört,
                    _ => SeriesStatusFilter.Alle
                };
            }
        }

        /// <summary>
        /// Provider-Suche bei Enter in der AutoSuggestBox.
        /// </summary>
        private void OnProviderSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            ViewModel.SearchText = args.QueryText;

            if (ViewModel.ProviderSearchCommand.CanExecute(null))
            {
                ViewModel.ProviderSearchCommand.Execute(null);
            }
        }

        /// <summary>
        /// Navigiert zu den Einstellungen (Leer-Zustand Button).
        /// </summary>
        private void OnGoToSettingsClick(object sender, RoutedEventArgs e)
        {
            _navigationService.NavigateTo(NavigationTarget.Settings);
        }

        /// <summary>
        /// Provider-Suche direkt starten (Leer-Zustand Button).
        /// </summary>
        /// <summary>
        /// Navigiert den Fokus zur Suchleiste, damit der Nutzer direkt einen Suchbegriff eingeben kann.
        /// Wenn bereits ein Suchtext vorhanden ist, wird die Suche sofort ausgelöst.
        /// </summary>
        private void OnAddSeriesClick(object sender, RoutedEventArgs e)
        {
            SearchBox.Focus(FocusState.Programmatic);

            if (!string.IsNullOrWhiteSpace(ViewModel.SearchText)
                && ViewModel.ProviderSearchCommand.CanExecute(null))
            {
                ViewModel.ProviderSearchCommand.Execute(null);
            }
        }

        /// <summary>
        /// Entfernt eine Online-Serie nach Bestätigung aus der Mediathek.
        /// Die Serien-ID wird über das Tag-Property des MenuFlyoutItem übergeben.
        /// </summary>
        /// <summary>
        /// Navigiert zur Serien-Detailansicht.
        /// </summary>
        private void OnSeriesDetailsClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is Guid seriesId)
            {
                _navigationService.NavigateTo(NavigationTarget.SeriesDetail, seriesId);
            }
        }

        /// <summary>
        /// Schaltet den Überwachungsstatus einer Serie um. Die Logik (Cache + iTunes-Check)
        /// liegt im <see cref="MediathekOnlineViewModel"/> bzw. dem zugehörigen Service.
        /// </summary>
        private async void OnToggleWatchSeriesClick(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleMenuFlyoutItem item && item.Tag is Guid seriesId)
            {
                await ViewModel.ToggleWatchAsync(seriesId, item.IsChecked);
            }
        }

        private async void OnRemoveOnlineSeriesClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is Guid seriesId)
            {
                await ViewModel.RemoveSeriesAsync(seriesId);
            }
        }

        /// <summary>
        /// Öffnet den Cover-Such-Dialog für eine Online-Episode.
        /// Nutzt den gleichen <see cref="Helpers.CoverSearchDialog"/> wie die lokale Mediathek.
        /// </summary>
        private void OnHelpClick(object sender, RoutedEventArgs e) => HelpTip.IsOpen = true;

        private async void OnOnlineEpisodeCoverSearchClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: Guid episodeId }) return;

            OnlineEpisodeCardViewModel? card = null;
            foreach (OnlineEpisodeCardViewModel ep in ViewModel.Episodes)
            {
                if (ep.EpisodeId == episodeId) { card = ep; break; }
            }

            if (card is null) return;

            using IServiceScope scope = App.Services.CreateScope();
            EchoPlay.LocalLibrary.Cover.ICoverSearchService? coverSearch =
                scope.ServiceProvider.GetService<EchoPlay.LocalLibrary.Cover.ICoverSearchService>();

            if (coverSearch is null) return;

            EchoPlay.LocalLibrary.Cover.CoverSearchResult? selected =
                await Helpers.CoverSearchDialog.ShowAsync(
                    card.Title,
                    (query, ct) => coverSearch.SearchAsync(query, ct),
                    Content.XamlRoot);

            if (selected is null) return;

            try
            {
                using System.Net.Http.HttpClient httpClient = new();
                byte[] coverBytes = await httpClient.GetByteArrayAsync(selected.FullUrl);

                CoverService coverService = App.Services.GetRequiredService<CoverService>();
                await coverService.SetEpisodeCoverAsync(episodeId, coverBytes);

                // UI direkt aktualisieren – Cover aus Bytes erstellen
                Microsoft.UI.Xaml.Media.Imaging.BitmapImage? image =
                    await CoverService.ConvertToBitmapAsync(coverBytes);

                if (image is not null)
                {
                    card.CoverImage = image;
                }
            }
            catch (Exception)
            {
                // Netzwerkfehler → Platzhalter bleibt
            }
        }
    }
}
