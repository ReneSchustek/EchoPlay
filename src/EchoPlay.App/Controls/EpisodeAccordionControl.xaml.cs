using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections;

namespace EchoPlay.App.Controls
{
    /// <summary>
    /// Wiederverwendbares Akkordeon für Episoden-Kacheln.
    /// Stellt Border (farbliche Abgrenzung, dynamische Breite) und GridView bereit.
    /// Der Header (Titel, Schließen, Sortierung) liegt auf der jeweiligen Page
    /// außerhalb des ScrollViewers, damit er beim Scrollen fixiert bleibt.
    /// </summary>
    public sealed partial class EpisodeAccordionControl : UserControl
    {
        /// <summary>Breite eines Kachel-Slots (140px Kachel + 8px Margin).</summary>
        private const double TileSlotWidth = 148.0;

        // Zuletzt berechnete Kacheln je Reihe; neu gesetzt wird die Breite nur, wenn sie wechseln.
        private int _lastTilesPerRow;

        /// <summary>
        /// Initialisiert das Akkordeon mit dynamischer Breitenberechnung.
        /// </summary>
        public EpisodeAccordionControl()
        {
            InitializeComponent();
            SizeChanged += OnControlSizeChanged;
        }

        /// <summary>
        /// Passt die Breite des Rahmens an die Zahl der Kacheln je Reihe an, damit er bündig
        /// mit der letzten Kachel abschließt. Gesetzt wird nur, wenn sich diese Zahl ändert.
        /// </summary>
        private void OnControlSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Vor dem ersten Layout ist die Breite 0; 800 ergibt eine brauchbare erste Zeilenbreite.
            double available = ActualWidth > 0 ? ActualWidth : 800;
            int tilesPerRow = Math.Max(1, (int)(available / TileSlotWidth));

            if (tilesPerRow != _lastTilesPerRow)
            {
                _lastTilesPerRow = tilesPerRow;
                // Vier Pixel über der reinen Kachelbreite fassen den Rahmen (zweimal ein Pixel)
                // samt Rundungsreserve.
                OuterBorder.Width = tilesPerRow * TileSlotWidth + 4;
            }
        }

        /// <summary>ItemsSource für das Episoden-GridView.</summary>
        public static readonly DependencyProperty EpisodesSourceProperty =
            DependencyProperty.Register(nameof(EpisodesSource), typeof(IEnumerable), typeof(EpisodeAccordionControl),
                new PropertyMetadata(null, (d, e) => ((EpisodeAccordionControl)d).EpisodesGridView.ItemsSource = (IEnumerable?)e.NewValue));

        /// <summary>ItemsSource für das Episoden-GridView.</summary>
        public IEnumerable? EpisodesSource
        {
            get => (IEnumerable?)GetValue(EpisodesSourceProperty);
            set => SetValue(EpisodesSourceProperty, value);
        }

        /// <summary>DataTemplate für die Episoden-Kacheln.</summary>
        public static readonly DependencyProperty EpisodeTemplateProperty =
            DependencyProperty.Register(nameof(EpisodeTemplate), typeof(DataTemplate), typeof(EpisodeAccordionControl),
                new PropertyMetadata(null, (d, e) => ((EpisodeAccordionControl)d).EpisodesGridView.ItemTemplate = (DataTemplate?)e.NewValue));

        /// <summary>DataTemplate für die Episoden-Kacheln.</summary>
        public DataTemplate? EpisodeTemplate
        {
            get => (DataTemplate?)GetValue(EpisodeTemplateProperty);
            set => SetValue(EpisodeTemplateProperty, value);
        }

        /// <summary>
        /// SelectionMode des internen GridViews. Die lokale Mediathek braucht Einzelauswahl, um die
        /// Spuren der gewählten Folge anzuzeigen.
        /// </summary>
        public static readonly DependencyProperty SelectionModeProperty =
            DependencyProperty.Register(nameof(SelectionMode), typeof(ListViewSelectionMode), typeof(EpisodeAccordionControl),
                new PropertyMetadata(ListViewSelectionMode.None, (d, e) => ((EpisodeAccordionControl)d).EpisodesGridView.SelectionMode = (ListViewSelectionMode)e.NewValue));

        /// <summary>SelectionMode.</summary>
        public ListViewSelectionMode SelectionMode
        {
            get => (ListViewSelectionMode)GetValue(SelectionModeProperty);
            set => SetValue(SelectionModeProperty, value);
        }

        /// <summary>Zugriff auf das interne GridView für SelectionChanged-Events.</summary>
        public GridView GridView => EpisodesGridView;
    }
}
