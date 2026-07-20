using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Windows.Input;

namespace EchoPlay.App.Controls
{
    /// <summary>
    /// Wiederverwendbare Serien-Kachel (140×140) für lokale und Online-Mediathek.
    /// Zeigt Cover-Bild, Text-Overlay (Titel + Untertitel), einen "..."-Kontextmenü-Button
    /// und optional einen Favoriten-Button. Das Kontextmenü wird von außen übergeben,
    /// damit jede Seite eigene Menüeinträge konfigurieren kann.
    /// </summary>
    public sealed partial class SeriesTileControl : UserControl
    {
        /// <summary>
        /// Initialisiert die Kachel.
        /// </summary>
        public SeriesTileControl()
        {
            InitializeComponent();
        }

        // ── Cover ───────────────────────────────────────────────────────────────

        /// <summary>Cover-Bild der Serie. Null zeigt den Platzhalter an.</summary>
        public static readonly DependencyProperty CoverImageProperty =
            DependencyProperty.Register(nameof(CoverImage), typeof(BitmapImage), typeof(SeriesTileControl),
                new PropertyMetadata(null, OnCoverImageChanged));

        /// <summary>Cover-Bild der Serie.</summary>
        public BitmapImage? CoverImage
        {
            get => (BitmapImage?)GetValue(CoverImageProperty);
            set => SetValue(CoverImageProperty, value);
        }

        private static void OnCoverImageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            SeriesTileControl control = (SeriesTileControl)d;
            BitmapImage? image = (BitmapImage?)e.NewValue;
            control.CoverImageElement.Source = image;
            control.PlaceholderBorder.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Text ────────────────────────────────────────────────────────────────

        /// <summary>Titel der Serie (obere Zeile im Overlay).</summary>
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(SeriesTileControl),
                new PropertyMetadata(string.Empty, (d, e) => ((SeriesTileControl)d).TitleText.Text = (string)e.NewValue));

        /// <summary>Titel der Serie.</summary>
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        /// <summary>Untertitel (untere Zeile im Overlay, z.B. Zähler).</summary>
        public static readonly DependencyProperty SubtitleProperty =
            DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(SeriesTileControl),
                new PropertyMetadata(string.Empty, (d, e) => ((SeriesTileControl)d).SubtitleText.Text = (string)e.NewValue));

        /// <summary>Untertitel der Serie.</summary>
        public string Subtitle
        {
            get => (string)GetValue(SubtitleProperty);
            set => SetValue(SubtitleProperty, value);
        }

        // ── Kontextmenü ─────────────────────────────────────────────────────────

        /// <summary>
        /// Das Flyout-Menü für den "..."-Button. Wird von der Page konfiguriert –
        /// lokale und Online-Mediathek haben unterschiedliche Menüeinträge.
        /// </summary>
        public static readonly DependencyProperty ContextFlyoutMenuProperty =
            DependencyProperty.Register(nameof(ContextFlyoutMenu), typeof(FlyoutBase), typeof(SeriesTileControl),
                new PropertyMetadata(null, OnContextFlyoutChanged));

        /// <summary>Flyout-Menü für den Kontextmenü-Button.</summary>
        public FlyoutBase? ContextFlyoutMenu
        {
            get => (FlyoutBase?)GetValue(ContextFlyoutMenuProperty);
            set => SetValue(ContextFlyoutMenuProperty, value);
        }

        private static void OnContextFlyoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            SeriesTileControl control = (SeriesTileControl)d;
            control.ContextMenuButton.Flyout = (FlyoutBase?)e.NewValue;
        }

        // ── Favoriten ───────────────────────────────────────────────────────────

        /// <summary>Sichtbarkeit des Favoriten-Buttons (nur lokale Mediathek).</summary>
        public static readonly DependencyProperty FavoriteVisibilityProperty =
            DependencyProperty.Register(nameof(FavoriteVisibility), typeof(Visibility), typeof(SeriesTileControl),
                new PropertyMetadata(Visibility.Collapsed, (d, e) => ((SeriesTileControl)d).FavoriteButton.Visibility = (Visibility)e.NewValue));

        /// <summary>Sichtbarkeit des Favoriten-Buttons.</summary>
        public Visibility FavoriteVisibility
        {
            get => (Visibility)GetValue(FavoriteVisibilityProperty);
            set => SetValue(FavoriteVisibilityProperty, value);
        }

        /// <summary>Glyph des Favoriten-Buttons (gefüllt/leer).</summary>
        public static readonly DependencyProperty FavoriteGlyphProperty =
            DependencyProperty.Register(nameof(FavoriteGlyph), typeof(string), typeof(SeriesTileControl),
                new PropertyMetadata(string.Empty, (d, e) => ((SeriesTileControl)d).FavoriteButton.Content = (string)e.NewValue));

        /// <summary>Glyph des Favoriten-Buttons.</summary>
        public string FavoriteGlyph
        {
            get => (string)GetValue(FavoriteGlyphProperty);
            set => SetValue(FavoriteGlyphProperty, value);
        }

        /// <summary>Command für den Favoriten-Button.</summary>
        public static readonly DependencyProperty FavoriteCommandProperty =
            DependencyProperty.Register(nameof(FavoriteCommand), typeof(ICommand), typeof(SeriesTileControl),
                new PropertyMetadata(null, (d, e) => ((SeriesTileControl)d).FavoriteButton.Command = (ICommand?)e.NewValue));

        /// <summary>Command für den Favoriten-Button.</summary>
        public ICommand? FavoriteCommand
        {
            get => (ICommand?)GetValue(FavoriteCommandProperty);
            set => SetValue(FavoriteCommandProperty, value);
        }

        // ── Überwachung (IsWatched) ─────────────────────────────────────────────

        /// <summary>Sichtbarkeit des Überwachungs-Icons (Auge).</summary>
        public static readonly DependencyProperty WatchedVisibilityProperty =
            DependencyProperty.Register(nameof(WatchedVisibility), typeof(Visibility), typeof(SeriesTileControl),
                new PropertyMetadata(Visibility.Collapsed, (d, e) => ((SeriesTileControl)d).WatchedIndicator.Visibility = (Visibility)e.NewValue));

        /// <summary>Sichtbarkeit des Überwachungs-Icons.</summary>
        public Visibility WatchedVisibility
        {
            get => (Visibility)GetValue(WatchedVisibilityProperty);
            set => SetValue(WatchedVisibilityProperty, value);
        }

        /// <summary>Glyph des Überwachungs-Icons (Auge).</summary>
        public static readonly DependencyProperty WatchedGlyphProperty =
            DependencyProperty.Register(nameof(WatchedGlyph), typeof(string), typeof(SeriesTileControl),
                new PropertyMetadata(string.Empty, (d, e) => ((SeriesTileControl)d).WatchedIndicator.Content = (string)e.NewValue));

        /// <summary>Glyph des Überwachungs-Icons.</summary>
        public string WatchedGlyph
        {
            get => (string)GetValue(WatchedGlyphProperty);
            set => SetValue(WatchedGlyphProperty, value);
        }

        // ── Ausgewählt-Indikator ────────────────────────────────────────────────

        /// <summary>Sichtbarkeit des V-Pfeils unter der Kachel (aktive Serie im Akkordeon).</summary>
        public static readonly DependencyProperty SelectedIndicatorVisibilityProperty =
            DependencyProperty.Register(nameof(SelectedIndicatorVisibility), typeof(Visibility), typeof(SeriesTileControl),
                new PropertyMetadata(Visibility.Collapsed, (d, e) => ((SeriesTileControl)d).SelectedIndicator.Visibility = (Visibility)e.NewValue));

        /// <summary>Sichtbarkeit des V-Pfeils.</summary>
        public Visibility SelectedIndicatorVisibility
        {
            get => (Visibility)GetValue(SelectedIndicatorVisibilityProperty);
            set => SetValue(SelectedIndicatorVisibilityProperty, value);
        }
    }
}
