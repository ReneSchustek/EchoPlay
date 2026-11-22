using EchoPlay.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EchoPlay.App.Controls
{
    /// <summary>
    /// Kachel einer Veröffentlichung aus dem Kalender. Zeigt nur an; den Klick behandelt die
    /// Seite, die die Kachel einbettet.
    /// </summary>
    public sealed partial class ReleaseTileControl : UserControl
    {
        /// <summary>Die anzuzeigende Veröffentlichung.</summary>
        public static readonly DependencyProperty ItemProperty =
            DependencyProperty.Register(nameof(Item), typeof(ReleaseCalendarItemViewModel), typeof(ReleaseTileControl),
                new PropertyMetadata(null));

        /// <summary>
        /// Initialisiert die Kachel.
        /// </summary>
        public ReleaseTileControl()
        {
            InitializeComponent();
        }

        /// <summary>Die anzuzeigende Veröffentlichung.</summary>
        public ReleaseCalendarItemViewModel? Item
        {
            get => (ReleaseCalendarItemViewModel?)GetValue(ItemProperty);
            set => SetValue(ItemProperty, value);
        }
    }
}
