using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Windows.Input;

namespace EchoPlay.App.Controls
{
    /// <summary>
    /// Wiederverwendbare Folgen-Kachel (120×120) für lokale und Online-Mediathek.
    /// Zeigt Cover, Text-Overlay (Statuszeile + Titel), Gehört-Häkchen
    /// und einen konfigurierbaren Button oben rechts.
    /// </summary>
    public sealed partial class EpisodeTileControl : UserControl
    {
        /// <summary>
        /// Initialisiert die Kachel.
        /// </summary>
        public EpisodeTileControl()
        {
            InitializeComponent();
        }

        // ── Kachel-Klick ────────────────────────────────────────────────────────

        /// <summary>Command beim Klick auf die gesamte Kachel (nicht den Aktions-Button).</summary>
        public static readonly DependencyProperty TileCommandProperty =
            DependencyProperty.Register(nameof(TileCommand), typeof(ICommand), typeof(EpisodeTileControl),
                new PropertyMetadata(null));

        /// <summary>Command beim Klick auf die Kachel.</summary>
        public ICommand? TileCommand
        {
            get => (ICommand?)GetValue(TileCommandProperty);
            set => SetValue(TileCommandProperty, value);
        }

        private void OnTileTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            // Nicht auslösen wenn der Aktions-Button geklickt wurde
            if (ReferenceEquals(e.OriginalSource, ActionButton) || ActionButton.IsPointerOver) return;

            TileCommand?.Execute(null);
        }

        // ── Cover ───────────────────────────────────────────────────────────────

        /// <summary>Cover-Bild der Episode. Null zeigt den Platzhalter.</summary>
        public static readonly DependencyProperty CoverImageProperty =
            DependencyProperty.Register(nameof(CoverImage), typeof(BitmapImage), typeof(EpisodeTileControl),
                new PropertyMetadata(null, OnCoverImageChanged));

        /// <summary>Cover-Bild der Episode.</summary>
        public BitmapImage? CoverImage
        {
            get => (BitmapImage?)GetValue(CoverImageProperty);
            set => SetValue(CoverImageProperty, value);
        }

        private static void OnCoverImageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            EpisodeTileControl control = (EpisodeTileControl)d;
            BitmapImage? image = (BitmapImage?)e.NewValue;
            control.CoverImageElement.Source = image;
            control.PlaceholderBorder.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Text ────────────────────────────────────────────────────────────────

        /// <summary>Titel der Episode (untere Zeile im Overlay).</summary>
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(EpisodeTileControl),
                new PropertyMetadata(string.Empty, (d, e) => ((EpisodeTileControl)d).TitleText.Text = (string)e.NewValue));

        /// <summary>Titel der Episode.</summary>
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        /// <summary>Statustext (obere Zeile im Overlay, z.B. Folgennummer oder Datum).</summary>
        public static readonly DependencyProperty StatusTextValueProperty =
            DependencyProperty.Register(nameof(StatusTextValue), typeof(string), typeof(EpisodeTileControl),
                new PropertyMetadata(string.Empty, (d, e) => ((EpisodeTileControl)d).StatusText.Text = (string)e.NewValue));

        /// <summary>Statustext der Episode.</summary>
        public string StatusTextValue
        {
            get => (string)GetValue(StatusTextValueProperty);
            set => SetValue(StatusTextValueProperty, value);
        }

        // ── Gehört-Häkchen ──────────────────────────────────────────────────────

        /// <summary>Sichtbarkeit des Gehört-Häkchens.</summary>
        public static readonly DependencyProperty CompletedVisibilityProperty =
            DependencyProperty.Register(nameof(CompletedVisibility), typeof(Visibility), typeof(EpisodeTileControl),
                new PropertyMetadata(Visibility.Collapsed, (d, e) => ((EpisodeTileControl)d).CompletedIcon.Visibility = (Visibility)e.NewValue));

        /// <summary>Sichtbarkeit des Gehört-Häkchens.</summary>
        public Visibility CompletedVisibility
        {
            get => (Visibility)GetValue(CompletedVisibilityProperty);
            set => SetValue(CompletedVisibilityProperty, value);
        }

        // ── Aktions-Button ──────────────────────────────────────────────────────

        /// <summary>Glyph des Aktions-Buttons (z.B. "..." oder Browser-Icon).</summary>
        public static readonly DependencyProperty ActionGlyphProperty =
            DependencyProperty.Register(nameof(ActionGlyph), typeof(string), typeof(EpisodeTileControl),
                new PropertyMetadata(null, OnActionGlyphChanged));

        /// <summary>Glyph des Aktions-Buttons.</summary>
        public string? ActionGlyph
        {
            get => (string?)GetValue(ActionGlyphProperty);
            set => SetValue(ActionGlyphProperty, value);
        }

        private static void OnActionGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            EpisodeTileControl control = (EpisodeTileControl)d;
            string? glyph = (string?)e.NewValue;
            control.ActionButton.Content = glyph;
            // Sichtbarkeit nur setzen wenn keine explizite ActionVisibility gebunden ist
            if (control.GetValue(ActionVisibilityProperty) is Visibility.Visible ||
                control.GetValue(ActionVisibilityProperty) is null)
            {
                control.ActionButton.Visibility = string.IsNullOrEmpty(glyph) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        /// <summary>Explizite Sichtbarkeit des Aktions-Buttons (überschreibt ActionGlyph-Logik).</summary>
        public static readonly DependencyProperty ActionVisibilityProperty =
            DependencyProperty.Register(nameof(ActionVisibility), typeof(Visibility), typeof(EpisodeTileControl),
                new PropertyMetadata(Visibility.Visible, (d, e) => ((EpisodeTileControl)d).ActionButton.Visibility = (Visibility)e.NewValue));

        /// <summary>Explizite Sichtbarkeit des Aktions-Buttons.</summary>
        public Visibility ActionVisibility
        {
            get => (Visibility)GetValue(ActionVisibilityProperty);
            set => SetValue(ActionVisibilityProperty, value);
        }

        /// <summary>Command für den Aktions-Button.</summary>
        public static readonly DependencyProperty ActionCommandProperty =
            DependencyProperty.Register(nameof(ActionCommand), typeof(ICommand), typeof(EpisodeTileControl),
                new PropertyMetadata(null, (d, e) => ((EpisodeTileControl)d).ActionButton.Command = (ICommand?)e.NewValue));

        /// <summary>Command für den Aktions-Button.</summary>
        public ICommand? ActionCommand
        {
            get => (ICommand?)GetValue(ActionCommandProperty);
            set => SetValue(ActionCommandProperty, value);
        }

        /// <summary>Flyout-Menü für den Aktions-Button (optional, statt Command).</summary>
        public static readonly DependencyProperty ActionFlyoutProperty =
            DependencyProperty.Register(nameof(ActionFlyout), typeof(FlyoutBase), typeof(EpisodeTileControl),
                new PropertyMetadata(null, OnActionFlyoutChanged));

        /// <summary>Flyout-Menü für den Aktions-Button.</summary>
        public FlyoutBase? ActionFlyout
        {
            get => (FlyoutBase?)GetValue(ActionFlyoutProperty);
            set => SetValue(ActionFlyoutProperty, value);
        }

        private static void OnActionFlyoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            EpisodeTileControl control = (EpisodeTileControl)d;
            control.ActionButton.Flyout = (FlyoutBase?)e.NewValue;
            // Button sichtbar machen wenn Flyout gesetzt
            if (e.NewValue is not null)
            {
                control.ActionButton.Visibility = Visibility.Visible;
            }
        }

        /// <summary>Tooltip für den Aktions-Button.</summary>
        public static readonly DependencyProperty ActionTooltipProperty =
            DependencyProperty.Register(nameof(ActionTooltip), typeof(string), typeof(EpisodeTileControl),
                new PropertyMetadata(null, (d, e) => ToolTipService.SetToolTip(((EpisodeTileControl)d).ActionButton, (string?)e.NewValue)));

        /// <summary>Tooltip für den Aktions-Button.</summary>
        public string? ActionTooltip
        {
            get => (string?)GetValue(ActionTooltipProperty);
            set => SetValue(ActionTooltipProperty, value);
        }
    }
}
