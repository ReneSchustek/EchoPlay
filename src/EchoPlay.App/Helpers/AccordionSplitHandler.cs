using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Zustandsbehafteter Handler für die Akkordeon-Split-Logik der Mediathek-Pages.
    /// Übernimmt die ItemsSource-Aufteilung in zwei <see cref="GridView"/>-Bereiche
    /// (Top und Bottom) sowie das Rekursions-Flag, das verhindert, dass programmgesteuerte
    /// Auswahl-Änderungen rekursiv erneute Splits auslösen.
    /// Wird von <see cref="EchoPlay.App.Views.MediathekLokalPage"/> und
    /// <see cref="EchoPlay.App.Views.MediathekOnlinePage"/> verwendet, statt die Logik
    /// in jeder Page einzeln zu führen.
    /// </summary>
    /// <typeparam name="T">Typ der Kachel-ViewModels (z.B. LocalArtistCardViewModel oder SeriesCardViewModel).</typeparam>
    public sealed class AccordionSplitHandler<T> where T : class
    {
        private readonly GridView _topGrid;
        private readonly GridView _bottomGrid;
        private readonly Func<IReadOnlyList<T>> _getItems;
        private readonly Func<int> _getSelectedIndex;
        private readonly Func<double> _getAvailableWidth;

        private bool _isUpdating;
        private int _lastTilesPerRow;

        /// <summary>
        /// Erzeugt einen Handler für ein konkretes Top/Bottom-Grid-Paar.
        /// </summary>
        /// <param name="topGrid">Das obere Serien-Grid.</param>
        /// <param name="bottomGrid">Das untere Serien-Grid.</param>
        /// <param name="getItems">Liefert die aktuelle vollständige Item-Liste aus dem ViewModel.</param>
        /// <param name="getSelectedIndex">Liefert den aktuell gewählten Index aus dem ViewModel (-1 wenn keiner).</param>
        /// <param name="getAvailableWidth">Liefert die verfügbare Breite für die Split-Berechnung in Pixel.</param>
        public AccordionSplitHandler(
            GridView topGrid,
            GridView bottomGrid,
            Func<IReadOnlyList<T>> getItems,
            Func<int> getSelectedIndex,
            Func<double> getAvailableWidth)
        {
            _topGrid = topGrid;
            _bottomGrid = bottomGrid;
            _getItems = getItems;
            _getSelectedIndex = getSelectedIndex;
            _getAvailableWidth = getAvailableWidth;
        }

        /// <summary>
        /// Gibt an, ob der Handler gerade einen Split anwendet. Pages können dieses Flag
        /// in <c>SelectionChanged</c>-Handlern abfragen, um Rekursionen zu verhindern.
        /// </summary>
        public bool IsUpdating => _isUpdating;

        /// <summary>
        /// Wendet die Aufteilung basierend auf dem aktuellen Item- und Auswahl-Stand an.
        /// Setzt während der Operation <see cref="IsUpdating"/> auf <see langword="true"/>.
        /// </summary>
        public void UpdateSplit()
        {
            _isUpdating = true;
            try
            {
                IReadOnlyList<T> all = _getItems();
                int selectedIndex = _getSelectedIndex();

                if (selectedIndex < 0 || all.Count == 0)
                {
                    _topGrid.ItemsSource = all;
                    _topGrid.SelectedItem = null;
                    _bottomGrid.ItemsSource = Array.Empty<T>();
                    _bottomGrid.SelectedItem = null;
                    return;
                }

                double availableWidth = _getAvailableWidth();
                int splitIndex = AccordionSplitHelper.CalculateSplitIndex(
                    selectedIndex, all.Count, availableWidth);

                (IReadOnlyList<T> top, IReadOnlyList<T> bottom) =
                    AccordionSplitHelper.Split(all, splitIndex);

                _topGrid.ItemsSource = top;
                _bottomGrid.ItemsSource = bottom;
                _bottomGrid.SelectedItem = null;
                _topGrid.SelectedItem = all[selectedIndex];
            }
            finally
            {
                _isUpdating = false;
            }
        }

        /// <summary>
        /// Wird bei <c>SizeChanged</c> aufgerufen. Berechnet neu nur dann, wenn sich
        /// die Anzahl der Kacheln pro Reihe tatsächlich verändert hat – das vermeidet
        /// unnötige Splits bei minimalen Größenänderungen (Debounce).
        /// </summary>
        public void HandleSizeChanged()
        {
            double availableWidth = _getAvailableWidth();
            int tilesPerRow = AccordionSplitHelper.CalculateTilesPerRow(availableWidth);

            if (tilesPerRow != _lastTilesPerRow)
            {
                _lastTilesPerRow = tilesPerRow;
                UpdateSplit();
            }
        }
    }
}
