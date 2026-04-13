using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections;

namespace EchoPlay.App.Controls
{
    /// <summary>
    /// Wiederverwendbare horizontale Kachelreihe für das Dashboard.
    /// Enthält einen ScrollViewer mit ItemsControl, links und rechts je einen Pfeil-Button
    /// und kapselt die komplette Scroll-Logik (Pfeil-Visibility, ChangeView, ScrollableWidth-Tracking).
    /// Aufrufer setzen nur <see cref="ItemsSource"/> und <see cref="ItemTemplate"/>.
    /// </summary>
    public sealed partial class HorizontalTileRowControl : UserControl
    {
        /// <summary>
        /// Scroll-Schritt in Pixeln beim Klick auf einen Pfeil-Button.
        /// 3 Kacheln × 168 px (160 px Kachel + 8 px Margin) = 504 px.
        /// </summary>
        private const double ScrollStepPixels = 504;

        /// <summary>
        /// Toleranz für die Pfeil-Visibility-Berechnung. Fängt DPI-Rundungsfehler ab.
        /// </summary>
        private const double VisibilityTolerance = 0.5;

        /// <summary>
        /// Initialisiert das Control und verdrahtet die ScrollViewer-Events.
        /// </summary>
        public HorizontalTileRowControl()
        {
            InitializeComponent();

            // ScrollableWidth ändert sich erst nachdem der Content tatsächlich gelayoutet ist –
            // PropertyChangedCallback fängt diesen Moment zuverlässig ab.
            _ = TilesScrollViewer.RegisterPropertyChangedCallback(
                ScrollViewer.ScrollableWidthProperty,
                (_, _) => UpdateArrowVisibility());

            TilesScrollViewer.ViewChanged += (_, _) => UpdateArrowVisibility();
            TilesScrollViewer.SizeChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateArrowVisibility);
        }

        /// <summary>
        /// Datenquelle für die Kachelreihe – wird an das interne ItemsControl weitergereicht.
        /// </summary>
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(
                nameof(ItemsSource),
                typeof(IEnumerable),
                typeof(HorizontalTileRowControl),
                new PropertyMetadata(null, OnItemsSourceChanged));

        /// <summary>Datenquelle für die Kachelreihe.</summary>
        public IEnumerable? ItemsSource
        {
            get => (IEnumerable?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            HorizontalTileRowControl control = (HorizontalTileRowControl)d;
            control.TilesItemsControl.ItemsSource = e.NewValue;
            // Nach einem ItemsSource-Wechsel kann sich die ScrollableWidth ändern –
            // der RegisterPropertyChangedCallback feuert dann automatisch und aktualisiert die Pfeile.
        }

        /// <summary>
        /// Template für die einzelnen Kacheln – wird an das interne ItemsControl weitergereicht.
        /// </summary>
        public static readonly DependencyProperty ItemTemplateProperty =
            DependencyProperty.Register(
                nameof(ItemTemplate),
                typeof(DataTemplate),
                typeof(HorizontalTileRowControl),
                new PropertyMetadata(null, OnItemTemplateChanged));

        /// <summary>Template für die einzelnen Kacheln.</summary>
        public DataTemplate? ItemTemplate
        {
            get => (DataTemplate?)GetValue(ItemTemplateProperty);
            set => SetValue(ItemTemplateProperty, value);
        }

        private static void OnItemTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            HorizontalTileRowControl control = (HorizontalTileRowControl)d;
            control.TilesItemsControl.ItemTemplate = (DataTemplate?)e.NewValue;
        }

        /// <summary>
        /// Setzt die Sichtbarkeit der beiden Pfeil-Buttons anhand der aktuellen Scroll-Position.
        /// Links-Pfeil sichtbar, sobald links Inhalt abgeschnitten ist;
        /// Rechts-Pfeil sichtbar, sobald rechts mindestens eine Kachel teilweise verdeckt ist.
        /// </summary>
        private void UpdateArrowVisibility()
        {
            double maxOffset = TilesScrollViewer.ScrollableWidth;
            double currentOffset = TilesScrollViewer.HorizontalOffset;

            LeftArrow.Visibility  = currentOffset > VisibilityTolerance ? Visibility.Visible : Visibility.Collapsed;
            RightArrow.Visibility = currentOffset < maxOffset - VisibilityTolerance ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnLeftArrowClick(object sender, RoutedEventArgs e)
        {
            double targetOffset = Math.Max(0, TilesScrollViewer.HorizontalOffset - ScrollStepPixels);
            _ = TilesScrollViewer.ChangeView(targetOffset, null, null);
        }

        private void OnRightArrowClick(object sender, RoutedEventArgs e)
        {
            double targetOffset = TilesScrollViewer.HorizontalOffset + ScrollStepPixels;
            _ = TilesScrollViewer.ChangeView(targetOffset, null, null);
        }
    }
}
