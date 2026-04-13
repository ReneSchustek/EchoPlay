using EchoPlay.App.Infrastructure;
using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Threading.Tasks;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Startseite der Anwendung. Zeigt Neuerscheinungen (aus Favoriten), Favoriten-Kacheln,
    /// laufende Episoden, zuletzt gehörte Serien und Ankündigungen.
    ///
    /// Beim ersten Start (keine abonnierte Serie) wird automatisch zur Suchseite navigiert,
    /// damit der Nutzer sofort Serien importieren kann.
    /// </summary>
    public sealed partial class DashboardPage : Page
    {
        private readonly INavigationService _navigationService;

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

            // handledEventsToo: true ist der Schlüssel – damit empfängt der MainScrollViewer
            // das Mausrad-Event auch dann, wenn ein innerer horizontaler ScrollViewer es
            // bereits als "handled" markiert hat. Ohne diesen Trick schlucken die horizontalen
            // ScrollViewer das vertikale Mausrad komplett (bekanntes WinUI-3-Problem).
            MainScrollViewer.AddHandler(
                PointerWheelChangedEvent,
                new PointerEventHandler(OnMainScrollViewerPointerWheelChanged),
                true);
        }

        /// <summary>
        /// Lädt die Episodenliste beim Navigieren auf die Seite.
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
        /// Wird beim Verlassen der Seite aufgerufen. Die Pfeil-Logik der Kachelreihen
        /// liegt im <see cref="EchoPlay.App.Controls.HorizontalTileRowControl"/>; beim Navigieren
        /// ist nichts mehr aufzuräumen.
        /// </summary>
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
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

        /// <summary>
        /// Empfängt Mausrad-Events auf dem vertikalen Haupt-ScrollViewer – auch wenn ein
        /// innerer horizontaler ScrollViewer das Event bereits als handled markiert hat.
        /// Scrollt den MainScrollViewer vertikal um das Mausrad-Delta.
        /// </summary>
        /// <param name="sender">Der MainScrollViewer.</param>
        /// <param name="e">Mausrad-Eventdaten.</param>
        private void OnMainScrollViewerPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            PointerPoint point = e.GetCurrentPoint(MainScrollViewer);
            int delta = point.Properties.MouseWheelDelta;

            if (delta != 0)
            {
                double targetOffset = MainScrollViewer.VerticalOffset - delta;
                _ = MainScrollViewer.ChangeView(null, targetOffset, null, true);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Wird aufgerufen, wenn das Favoriten-ListView fertig geladen ist.
        /// Ermittelt die interne ScrollViewer-Instanz per VisualTreeHelper, damit die
        /// Pfeil-Button-Logik der Favoriten-Sektion (ListView mit Drag-and-Drop) funktioniert.
        /// Die anderen Kachelreihen nutzen das <see cref="Controls.HorizontalTileRowControl"/>
        /// und brauchen diese Logik nicht.
        /// </summary>
        private void OnFavoritesListViewLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ListView listView)
            {
                return;
            }

            ScrollViewer? scrollViewer = FindScrollViewerInElement(listView);

            if (scrollViewer is null)
            {
                return;
            }

            scrollViewer.ViewChanged += OnFavoritesViewChanged;
            scrollViewer.SizeChanged += (_, _) => DispatcherQueue.TryEnqueue(() => UpdateFavoritesArrowVisibility(scrollViewer));

            _ = scrollViewer.RegisterPropertyChangedCallback(
                ScrollViewer.ScrollableWidthProperty,
                (_, _) => UpdateFavoritesArrowVisibility(scrollViewer));

            _ = DispatcherQueue.TryEnqueue(() => UpdateFavoritesArrowVisibility(scrollViewer));
        }

        /// <summary>
        /// Aktualisiert die Pfeil-Sichtbarkeit der Favoriten-Sektion nach Scroll-Events.
        /// </summary>
        private void OnFavoritesViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                UpdateFavoritesArrowVisibility(scrollViewer);
            }
        }

        /// <summary>
        /// Klick auf die Favoriten-Pfeil-Buttons. Scrollt das interne ScrollViewer des
        /// Favoriten-ListViews horizontal um einen Schritt in Pfeil-Richtung.
        /// </summary>
        private void OnFavoritesArrowClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Parent is not Grid parentGrid)
            {
                return;
            }

            ScrollViewer? scrollViewer = null;
            foreach (Microsoft.UI.Xaml.UIElement child in parentGrid.Children)
            {
                if (child is FrameworkElement fe && Grid.GetColumn(fe) == 1 && fe is ListView listView)
                {
                    scrollViewer = FindScrollViewerInElement(listView);
                    break;
                }
            }

            if (scrollViewer is null)
            {
                return;
            }

            const double scrollStep = 504;
            string direction = button.Tag as string ?? "right";
            double targetOffset = direction == "left"
                ? Math.Max(0, scrollViewer.HorizontalOffset - scrollStep)
                : scrollViewer.HorizontalOffset + scrollStep;

            _ = scrollViewer.ChangeView(targetOffset, null, null);
        }

        /// <summary>
        /// Setzt die Sichtbarkeit der beiden Pfeile der Favoriten-Sektion.
        /// </summary>
        private void UpdateFavoritesArrowVisibility(ScrollViewer scrollViewer)
        {
            // Geht hoch zum 3-Spalten-Grid: ScrollViewer → ... → ListView → Grid
            DependencyObject current = scrollViewer;
            Grid? parentGrid = null;
            for (int i = 0; i < 12 && current is not null; i++)
            {
                current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
                if (current is Grid g && g.ColumnDefinitions.Count == 3)
                {
                    parentGrid = g;
                    break;
                }
            }

            if (parentGrid is null)
            {
                return;
            }

            double maxOffset = scrollViewer.ScrollableWidth;
            double currentOffset = scrollViewer.HorizontalOffset;
            bool showLeft = currentOffset > 0.5;
            bool showRight = currentOffset < maxOffset - 0.5;

            foreach (Microsoft.UI.Xaml.UIElement child in parentGrid.Children)
            {
                if (child is Button arrowButton && arrowButton.Tag is string tag)
                {
                    arrowButton.Visibility = (tag == "left" ? showLeft : showRight)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// Durchsucht den visuellen Baum eines Elements rekursiv nach dem ersten ScrollViewer.
        /// Wird benötigt, um die interne ScrollViewer-Instanz des Favoriten-ListViews zu finden,
        /// die nicht direkt über das Control-API zugänglich ist.
        /// </summary>
        private static ScrollViewer? FindScrollViewerInElement(DependencyObject element)
        {
            int childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element);

            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, i);

                if (child is ScrollViewer sv)
                {
                    return sv;
                }

                ScrollViewer? found = FindScrollViewerInElement(child);
                if (found is not null)
                {
                    return found;
                }
            }

            return null;
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
            if (!ViewModel.HasSubscribedSeries)
            {
                _navigationService.NavigateTo(NavigationTarget.Suche, "onboarding");
                return;
            }

            // Pfeil-Visibility für die Kachelreihen wird vom HorizontalTileRowControl
            // selbst über RegisterPropertyChangedCallback auf ScrollableWidth gesetzt –
            // hier ist nichts mehr zu tun.
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
