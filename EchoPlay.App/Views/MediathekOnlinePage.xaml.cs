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

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Online-Mediathek mit Akkordeon-Layout.
    /// Serien als Cover-Kachelgrid, Folgen klappen unterhalb der gewählten Reihe auf.
    /// Unterstützt Inline-Provider-Suche zum Hinzufügen neuer Serien.
    /// </summary>
    public sealed partial class MediathekOnlinePage : Page
    {
        /// <summary>
        /// Akkordeon-Split-Handler kapselt die Top/Bottom-Aufteilung der Serien-Kacheln
        /// und das Rekursions-Flag (vorher direkt in der Page geführt).
        /// </summary>
        private readonly Helpers.AccordionSplitHandler<SeriesCardViewModel> _splitHandler;

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

            _splitHandler = new Helpers.AccordionSplitHandler<SeriesCardViewModel>(
                SeriesTopGrid,
                SeriesBottomGrid,
                () => ViewModel.Series,
                () => ViewModel.SelectedSeriesIndex,
                () => Math.Max(Helpers.AccordionSplitHelper.SeriesTileSlotWidth, ActualWidth - 32));
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

            ViewModel.PropertyChanged    += OnViewModelPropertyChanged;
            ViewModel.FocusSearchRequested += OnFocusSearchRequested;
            SizeChanged                  += OnPageSizeChanged;

            await ViewModel.LoadAsync();
        }

        /// <summary>
        /// Deabonniert Events beim Verlassen der Seite.
        /// </summary>
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            ViewModel.PropertyChanged    -= OnViewModelPropertyChanged;
            ViewModel.FocusSearchRequested -= OnFocusSearchRequested;
            SizeChanged                  -= OnPageSizeChanged;
        }

        /// <summary>
        /// Setzt den Fokus auf die Suchbox – wird vom <see cref="MediathekOnlineViewModel.FocusSearchRequested"/>-Event
        /// ausgelöst, wenn der Empty-State-Button "Serie suchen" geklickt wurde.
        /// </summary>
        private void OnFocusSearchRequested()
        {
            SearchBox.Focus(FocusState.Programmatic);
        }

        // ── Akkordeon Split-Logik ────────────────────────────────────────────────

        /// <summary>
        /// Reagiert auf Änderungen der Serienliste oder des Auswahl-Index und delegiert
        /// die Top/Bottom-Aufteilung an den <see cref="_splitHandler"/>.
        /// </summary>
        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MediathekOnlineViewModel.Series)
                               or nameof(MediathekOnlineViewModel.SelectedSeriesIndex))
            {
                _splitHandler.UpdateSplit();
            }
        }

        /// <summary>
        /// Bei Fenstergrößenänderung den Split neu berechnen.
        /// </summary>
        private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
        {
            _splitHandler.HandleSizeChanged();
        }

        // ── Event-Handler ────────────────────────────────────────────────────────

        /// <summary>
        /// Wird ausgelöst wenn der Nutzer eine Serien-Kachel klickt.
        /// Lädt die Episoden und öffnet das Akkordeon.
        /// </summary>
        private async void OnSeriesItemClick(object sender, ItemClickEventArgs e)
        {
            if (_splitHandler.IsUpdating) return;

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
        /// Such- und Apply-Logik liegt im ViewModel; die Page weiß weder etwas vom DI-Scope
        /// noch vom LocalLibrary-Modell.
        /// </summary>
        private async void OnOnlineEpisodeCoverSearchClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: Guid episodeId }) return;

            OnlineEpisodeCardViewModel? card = null;
            foreach (OnlineEpisodeCardViewModel ep in ViewModel.Episodes)
            {
                if (ep.EpisodeId == episodeId) { card = ep; break; }
            }

            if (card is null) return;

            CoverSearchHit? selected = await Helpers.CoverSearchDialog.ShowAsync(
                card.Title,
                (query, ct) => ViewModel.SearchEpisodeCoversAsync(query, ct),
                Content.XamlRoot);

            if (selected is null) return;

            await ViewModel.ApplySelectedEpisodeCoverAsync(card, selected);
        }
    }
}
