using EchoPlay.App.Services;
using EchoPlay.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Pages
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
        /// <summary>
        /// Scroll-Schritt in Pixeln beim Klick auf einen Pfeil-Button.
        /// Berechnung: 3 Kacheln × 168 px (160 px Kachel + 8 px Margin) = 504 px.
        /// </summary>
        private const double ScrollStepPixels = 504;

        /// <summary>
        /// Alle registrierten horizontalen Kachelreihen-ScrollViewer.
        /// Wird gebraucht, um nach dem initialen Laden alle Pfeil-Buttons
        /// zentral zu aktualisieren, wenn das Layout vollständig steht.
        /// </summary>
        private readonly List<ScrollViewer> _tileRowScrollViewers = new();

        /// <summary>Gibt dem XAML-Compiler Zugriff auf das ViewModel für x:Bind.</summary>
        public DashboardViewModel ViewModel { get; }

        /// <summary>
        /// Initialisiert die Seite und bezieht das ViewModel aus dem DI-Container.
        /// </summary>
        public DashboardPage()
        {
            ViewModel = App.Services.GetRequiredService<DashboardViewModel>();
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
            await LoadAndCheckOnboardingAsync();
        }

        /// <summary>
        /// Räumt registrierte Event-Handler der Kachelreihen-ScrollViewer auf.
        /// Verhindert Memory-Leaks bei wiederholter Navigation zum Dashboard.
        /// </summary>
        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            foreach (ScrollViewer scrollViewer in _tileRowScrollViewers)
            {
                scrollViewer.ViewChanged -= OnTileRowViewChanged;
                scrollViewer.SizeChanged -= OnTileRowSizeChanged;
            }

            _tileRowScrollViewers.Clear();
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
                Frame.Navigate(typeof(SeriesDetailPage), seriesId);
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
                Frame.Navigate(typeof(SeriesDetailPage), card.SeriesId);
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
                Frame.Navigate(typeof(SeriesDetailPage), seriesId);
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
                MainScrollViewer.ChangeView(null, targetOffset, null, true);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Wird aufgerufen, wenn ein horizontaler Kachelreihen-ScrollViewer fertig geladen ist.
        /// Registriert den ViewChanged-Handler, der die Pfeil-Visibility aktualisiert,
        /// und führt eine initiale Prüfung durch.
        /// </summary>
        /// <param name="sender">Der geladene ScrollViewer.</param>
        /// <param name="e">Ereignisargumente.</param>
        private void OnTileRowScrollViewerLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                _tileRowScrollViewers.Add(scrollViewer);
                scrollViewer.ViewChanged += OnTileRowViewChanged;
                scrollViewer.SizeChanged += OnTileRowSizeChanged;

                // ScrollableWidth ändert sich erst, wenn der Content (Kacheln) tatsächlich
                // gelayoutet ist – das passiert nach LoadAsync, also deutlich nach Loaded/SizeChanged.
                // Per PropertyChanged-Callback reagieren wir zuverlässig auf den Moment,
                // in dem der Inhalt breiter wird als der sichtbare Bereich.
                scrollViewer.RegisterPropertyChangedCallback(
                    ScrollViewer.ScrollableWidthProperty,
                    OnScrollableWidthChanged);

                // Bei Kachelreihen, die erst durch späteres Daten-Binding erzeugt werden
                // (z.B. Neuerscheinungen aus dem DB-Cache), kann ScrollableWidth beim Loaded
                // bereits den korrekten Wert haben – dann feuert der PropertyChangedCallback nicht.
                // Verzögerter Check stellt sicher, dass die Pfeile auch in diesem Fall erscheinen.
                DispatcherQueue.TryEnqueue(() => UpdateArrowVisibility(scrollViewer));
            }
        }

        /// <summary>
        /// Reagiert auf Änderungen an <see cref="ScrollViewer.ScrollableWidthProperty"/>.
        /// Feuert z.B. wenn nach dem asynchronen Laden neue Kacheln eingefügt werden und
        /// der Content erstmals breiter wird als der sichtbare Bereich.
        /// </summary>
        /// <param name="sender">Der horizontale ScrollViewer der Kachelreihe.</param>
        /// <param name="dp">Die geänderte DependencyProperty (ScrollableWidth).</param>
        private void OnScrollableWidthChanged(DependencyObject sender, DependencyProperty dp)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                UpdateArrowVisibility(scrollViewer);
            }
        }

        /// <summary>
        /// Aktualisiert die Pfeil-Visibility nach einer Größenänderung (z.B. Fenster-Resize).
        /// Per DispatcherQueue verzögert, weil Layout-Werte wie ScrollableWidth erst nach
        /// Abschluss des aktuellen Layout-Passes stimmen – z.B. wenn der vertikale Scrollbar
        /// des MainScrollViewer erscheint und die horizontalen Reihen schmaler werden.
        /// </summary>
        private void OnTileRowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                DispatcherQueue.TryEnqueue(() => UpdateArrowVisibility(scrollViewer));
            }
        }

        /// <summary>
        /// Aktualisiert die Pfeil-Visibility nach horizontalem Scrollen.
        /// </summary>
        private void OnTileRowViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                UpdateArrowVisibility(scrollViewer);
            }
        }

        /// <summary>
        /// Scrollt die Kachelreihe horizontal um einen Schritt in die Richtung des Pfeils.
        /// Der Pfeil-Button hat "left" oder "right" als Tag. Der zugehörige ScrollViewer
        /// ist das Geschwister-Element in Column 1 des umgebenden Grids.
        /// </summary>
        /// <param name="sender">Der angeklickte Pfeil-Button.</param>
        /// <param name="e">Ereignisargumente.</param>
        private void OnScrollArrowClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Parent is not Grid parentGrid)
            {
                return;
            }

            // ScrollViewer ist immer in Column 1 des 3-Spalten-Grids
            ScrollViewer? scrollViewer = FindScrollViewerInGrid(parentGrid);
            if (scrollViewer is null)
            {
                return;
            }

            string direction = button.Tag as string ?? "right";
            double targetOffset = direction == "left"
                ? Math.Max(0, scrollViewer.HorizontalOffset - ScrollStepPixels)
                : scrollViewer.HorizontalOffset + ScrollStepPixels;

            scrollViewer.ChangeView(targetOffset, null, null);
        }

        /// <summary>
        /// Setzt die Sichtbarkeit der Pfeil-Buttons neben einer Kachelreihe.
        /// Links-Pfeil erscheint sobald Inhalt links abgeschnitten ist,
        /// Rechts-Pfeil sobald rechts mindestens ein Cover nicht vollständig sichtbar ist.
        /// </summary>
        /// <param name="scrollViewer">Der horizontale ScrollViewer der Kachelreihe.</param>
        private static void UpdateArrowVisibility(ScrollViewer scrollViewer)
        {
            if (scrollViewer.Parent is not Grid parentGrid)
            {
                return;
            }

            double maxOffset = scrollViewer.ScrollableWidth;
            double currentOffset = scrollViewer.HorizontalOffset;

            // Pfeile einblenden sobald der Inhalt auch nur teilweise abgeschnitten ist.
            // Toleranz 0.5 px fängt DPI-Rundungsfehler ab, ohne echte Überlappung zu verschlucken.
            bool showLeft = currentOffset > 0.5;
            bool showRight = currentOffset < maxOffset - 0.5;

            foreach (Microsoft.UI.Xaml.UIElement child in parentGrid.Children)
            {
                if (child is Button arrowButton && arrowButton.Tag is string tag)
                {
                    if (tag == "left")
                    {
                        arrowButton.Visibility = showLeft ? Visibility.Visible : Visibility.Collapsed;
                    }
                    else if (tag == "right")
                    {
                        arrowButton.Visibility = showRight ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
            }
        }

        /// <summary>
        /// Wird aufgerufen, wenn das Favoriten-ListView fertig geladen ist.
        /// Ermittelt die interne ScrollViewer-Instanz des ListViews per VisualTreeHelper,
        /// damit die Pfeil-Button-Logik auch für die Favoriten-Kachelreihe funktioniert.
        /// </summary>
        /// <param name="sender">Das geladene ListView.</param>
        /// <param name="e">Ereignisargumente.</param>
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

            // Dieselbe Infrastruktur wie für die anderen Kachelreihen verwenden
            _tileRowScrollViewers.Add(scrollViewer);
            scrollViewer.ViewChanged += OnTileRowViewChanged;
            scrollViewer.SizeChanged += OnTileRowSizeChanged;

            scrollViewer.RegisterPropertyChangedCallback(
                ScrollViewer.ScrollableWidthProperty,
                OnScrollableWidthChanged);
        }

        /// <summary>
        /// Sucht den ScrollViewer im 3-Spalten-Grid (immer in Column 1).
        /// Unterstützt sowohl direkte ScrollViewer als auch ListViews,
        /// deren interner ScrollViewer per VisualTreeHelper ermittelt wird.
        /// </summary>
        /// <param name="grid">Das übergeordnete Grid.</param>
        /// <returns>Den gefundenen ScrollViewer oder null.</returns>
        private static ScrollViewer? FindScrollViewerInGrid(Grid grid)
        {
            foreach (Microsoft.UI.Xaml.UIElement child in grid.Children)
            {
                // Grid.GetColumn erwartet FrameworkElement – UIElements ohne Layout-Spalte ignorieren
                if (child is not FrameworkElement fe || Grid.GetColumn(fe) != 1)
                {
                    continue;
                }

                if (fe is ScrollViewer sv)
                {
                    return sv;
                }

                // Favoriten-Sektion: ListView in Column 1 statt direktem ScrollViewer
                if (fe is ListView listView)
                {
                    return FindScrollViewerInElement(listView);
                }
            }

            return null;
        }

        /// <summary>
        /// Durchsucht den visuellen Baum eines Elements rekursiv nach dem ersten ScrollViewer.
        /// Wird benötigt, um die interne ScrollViewer-Instanz eines ListViews zu finden,
        /// die nicht direkt über das Control-API zugänglich ist.
        /// </summary>
        /// <param name="element">Das Startelement der Suche.</param>
        /// <returns>Den ersten gefundenen ScrollViewer oder null.</returns>
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

                // Rekursiv in Kindelementen suchen
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
                Frame.Navigate(typeof(SuchePage), "onboarding");
                return;
            }

            // Doppelter Dispatch: Der erste Tick wartet, bis alle DataTemplates instanziiert
            // und Bindings ausgewertet sind. Der zweite Tick stellt sicher, dass der vertikale
            // Scrollbar des MainScrollViewer sichtbar ist und die endgültige Breite feststeht.
            DispatcherQueue.TryEnqueue(() =>
            {
                DispatcherQueue.TryEnqueue(UpdateAllTileRowArrows);
            });
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

        /// <summary>
        /// Aktualisiert die Pfeil-Sichtbarkeit aller registrierten Kachelreihen.
        /// Wird nach dem initialen Laden aufgerufen, wenn das Layout vollständig steht.
        /// </summary>
        private void UpdateAllTileRowArrows()
        {
            foreach (ScrollViewer scrollViewer in _tileRowScrollViewers)
            {
                UpdateArrowVisibility(scrollViewer);
            }
        }

        private void OnHelpClick(object sender, RoutedEventArgs e) => HelpTip.IsOpen = true;
    }
}
