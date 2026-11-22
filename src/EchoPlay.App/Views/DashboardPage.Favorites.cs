using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Das Favoriten-Band der Startseite: waagerechtes Blättern mit Pfeilen und Mausrad, ohne
    /// dass das senkrechte Blättern der Seite verloren geht.
    /// </summary>
    public sealed partial class DashboardPage : Page
    {
        /// <summary>
        /// Empfängt Mausrad-Events auf dem vertikalen Haupt-ScrollViewer, auch wenn ein
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

            // Referenz halten, damit OnNavigatedFrom die Subscriptions wieder lösen kann
            // und kein Handler am toten ScrollViewer hängen bleibt.
            _favoritesScrollViewer = scrollViewer;
            scrollViewer.ViewChanged += OnFavoritesViewChanged;
            scrollViewer.SizeChanged += OnFavoritesScrollViewerSizeChanged;

            _favoritesScrollableWidthCallbackToken = scrollViewer.RegisterPropertyChangedCallback(
                ScrollViewer.ScrollableWidthProperty,
                OnFavoritesScrollableWidthChanged);

            _ = DispatcherQueue.TryEnqueue(() => UpdateFavoritesArrowVisibility(scrollViewer));
        }

        /// <summary>
        /// Reagiert auf Größenänderungen des Favoriten-ScrollViewers (Window-Resize),
        /// damit die Pfeil-Sichtbarkeit nachgeführt wird. Benannt statt Lambda,
        /// damit OnNavigatedFrom die Subscription sicher abmelden kann.
        /// </summary>
        private void OnFavoritesScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_favoritesScrollViewer is { } scrollViewer)
            {
                _ = DispatcherQueue.TryEnqueue(() => UpdateFavoritesArrowVisibility(scrollViewer));
            }
        }

        /// <summary>
        /// Reagiert auf Änderungen der ScrollableWidth (z. B. neu geladene Items),
        /// damit die Pfeile sofort eingeblendet werden, sobald horizontaler Platz fehlt.
        /// </summary>
        private void OnFavoritesScrollableWidthChanged(DependencyObject sender, DependencyProperty dp)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                UpdateFavoritesArrowVisibility(scrollViewer);
            }
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

            // Knapp drei Kacheln; eine Kachel ist mit Abstand 172 Pixel breit (160 plus 12).
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
            // Die Grenze hält die Suche in der Nähe der Liste; ein Raster mit drei Spalten weiter
            // oben im Fenster wäre der falsche Treffer.
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
            // Ein halber Pixel Toleranz, weil der Versatz nach dem Scrollen selten genau auf 0 oder
            // dem Maximum landet.
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
    }
}
