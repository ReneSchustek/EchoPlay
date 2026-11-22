using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System.Threading.Tasks;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Startseite der Anwendung. Zeigt Neuerscheinungen und Ankündigungen überwachter Serien,
    /// Favoriten-Kacheln, „Weiterhören“, laufende Episoden und zuletzt gehörte Serien.
    ///
    /// Beim ersten Start (keine abonnierte Serie) wird automatisch zur Suchseite navigiert,
    /// damit der Nutzer sofort Serien importieren kann.
    /// </summary>
    public sealed partial class DashboardPage : Page
    {
        private readonly INavigationService _navigationService;
        private ScrollViewer? _favoritesScrollViewer;
        private long _favoritesScrollableWidthCallbackToken;

        /// <summary>Gibt dem XAML-Compiler Zugriff auf das ViewModel für x:Bind.</summary>
        public DashboardViewModel ViewModel { get; }

        /// <summary>
        /// Initialisiert die Seite und bezieht das ViewModel aus dem DI-Container.
        /// </summary>
        public DashboardPage()
        {
            ViewModel = App.Services.GetRequiredService<DashboardViewModel>();
            _navigationService = App.Services.GetRequiredService<INavigationService>();
            InitializeComponent();

            // handledEventsToo: true ist der Schlüssel: Damit empfängt der MainScrollViewer
            // das Mausrad-Event auch dann, wenn ein innerer horizontaler ScrollViewer es
            // bereits als "handled" markiert hat. Ohne diesen Trick schlucken die horizontalen
            // ScrollViewer das vertikale Mausrad komplett (bekanntes WinUI-3-Problem).
            MainScrollViewer.AddHandler(
                PointerWheelChangedEvent,
                new PointerEventHandler(OnMainScrollViewerPointerWheelChanged),
                true);
        }

        /// <summary>
        /// Lädt die Abschnitte der Startseite beim Navigieren auf die Seite.
        /// Sind keine abonnierten Serien vorhanden (erster Start oder leere Datenbank),
        /// wird automatisch zur Suchseite navigiert, damit der Nutzer Serien importieren kann.
        /// </summary>
        /// <param name="e">Navigationsparameter (werden nicht verwendet).</param>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await AsyncEventHandler.RunSafelyAsync(LoadAndCheckOnboardingAsync);
        }

        /// <summary>
        /// Meldet alle Page-Subscriptions sauber ab und disposed das ViewModel.
        /// Die Pfeil-Logik der Kachelreihen liegt im <see cref="EchoPlay.App.Controls.HorizontalTileRowControl"/>
        /// und bleibt dort; hier geht es um die Favoriten-Scroll-Subscription (außerhalb des Controls)
        /// und die IDisposable-VM-Kette.
        /// </summary>
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            if (_favoritesScrollViewer is not null)
            {
                _favoritesScrollViewer.ViewChanged -= OnFavoritesViewChanged;
                _favoritesScrollViewer.SizeChanged -= OnFavoritesScrollViewerSizeChanged;
                _favoritesScrollViewer.UnregisterPropertyChangedCallback(
                    ScrollViewer.ScrollableWidthProperty,
                    _favoritesScrollableWidthCallbackToken);
                _favoritesScrollViewer = null;
            }

            ViewModel.Dispose();
        }

        /// <summary>
        /// Navigiert zur Seriendetailseite, wenn eine „Zuletzt gehört"-Kachel angeklickt wird.
        /// Die SeriesId wird als Tag des Buttons übergeben.
        /// </summary>
        /// <param name="sender">Der Button der Kachel.</param>
        /// <param name="e">Ereignisargumente.</param>
        private void OnRecentSeriesClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: Guid seriesId })
            {
                _navigationService.NavigateTo(NavigationTarget.SeriesDetail, seriesId);
            }
        }

        /// <summary>
        /// Navigiert zur Seriendetailseite, wenn eine Favoriten-Kachel angeklickt wird.
        /// Verwendet <c>ListView.ItemClick</c> statt eines Buttons im DataTemplate,
        /// weil ein Full-Size-Button alle Pointer-Events schluckt und damit das
        /// Drag-&amp;-Drop-Reordering des ListViews verhindert.
        /// </summary>
        /// <param name="sender">Das Favoriten-ListView.</param>
        /// <param name="e">Enthält das angeklickte <see cref="FavoriteSeriesCardViewModel"/>.</param>
        private void OnFavoriteSeriesItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is FavoriteSeriesCardViewModel card)
            {
                _navigationService.NavigateTo(NavigationTarget.SeriesDetail, card.SeriesId);
            }
        }

        /// <summary>
        /// Navigiert zur Seriendetailseite, wenn eine "Weiterhören"-Kachel angeklickt wird.
        /// </summary>
        /// <param name="sender">Der Button der Kachel.</param>
        /// <param name="e">Ereignisargumente.</param>
        private void OnUnheardSeriesClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: Guid seriesId })
            {
                _navigationService.NavigateTo(NavigationTarget.SeriesDetail, seriesId);
            }
        }

        /// <summary>Öffnet die Serie einer Kachel aus dem Veröffentlichungskalender.</summary>
        /// <param name="sender">Die Schaltfläche der Kachel; ihr Tag trägt die Serien-Kennung.</param>
        /// <param name="e">Ereignisdaten.</param>
        private void OnReleaseCalendarItemClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: Guid seriesId })
            {
                _navigationService.NavigateTo(NavigationTarget.SeriesDetail, seriesId);
            }
        }

        /// <summary>
        /// Lädt die Dashboard-Daten und prüft ob eine Onboarding-Weiterleitung nötig ist.
        /// Wird als eigene Methode ausgelagert, damit <c>async void</c> auf das Minimum beschränkt bleibt.
        /// </summary>
        private async Task LoadAndCheckOnboardingAsync()
        {
            // Startup-Hinweise aus dem Splash-Ergebnis anzeigen
            ShowStartupHints();

            await ViewModel.LoadAsync();

            // Keine abonnierte Serie → direkt zur Suche, damit der Nutzer loslegen kann
            if (!ViewModel.Hints.HasSubscribedSeries)
            {
                _navigationService.NavigateTo(NavigationTarget.Search, "onboarding");
                return;
            }
        }

        /// <summary>
        /// Zeigt Startup-Hinweise an, wenn der Splash-Check Probleme erkannt hat.
        /// Die Texte kommen aus den Resource-Strings, der Zustand aus dem <see cref="Services.StartupResult"/>.
        /// </summary>
        private void ShowStartupHints()
        {
            StartupResult? result = App.StartupResultData;
            if (result is null) return;

            if (result.OnlineHintText is not null)
            {
                OnlineHintBar.IsOpen = true;
            }

            if (result.LocalLibraryHintText is not null)
            {
                LocalLibraryHintBar.IsOpen = true;
            }
        }
    }
}
