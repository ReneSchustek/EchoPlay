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

        /// <summary>Debounce für SizeChanged – nur bei Änderung der Kachelanzahl pro Reihe.</summary>
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
        /// Passt die Border-Breite dynamisch an die Kachelanzahl pro Reihe an.
        /// Debounce: nur bei Änderung der Kachelanzahl.
        /// </summary>
        private void OnControlSizeChanged(object sender, SizeChangedEventArgs e)
        {
            double available = ActualWidth > 0 ? ActualWidth : 800;
            int tilesPerRow = Math.Max(1, (int)(available / TileSlotWidth));

            if (tilesPerRow != _lastTilesPerRow)
            {
                _lastTilesPerRow = tilesPerRow;
                OuterBorder.Width = tilesPerRow * TileSlotWidth + 4;
            }
        }

        // ── Episoden ────────────────────────────────────────────────────────────

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

        // ── GridView-Zugriff ────────────────────────────────────────────────────

        /// <summary>
        /// SelectionMode des internen GridViews – lokal braucht Single-Selection für Track-Anzeige.
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
