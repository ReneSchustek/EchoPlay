using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Collections.Generic;

namespace EchoPlay.App.Controls
{
    /// <summary>
    /// Filterleiste der Gestaltungslinie. Meldet über <see cref="FilterToggled"/>, welcher
    /// Filter umgeschaltet wurde; was das für die Liste bedeutet, entscheidet die Seite.
    /// </summary>
    public sealed partial class FilterBarControl : UserControl
    {
        /// <summary>
        /// Initialisiert die Filterleiste.
        /// </summary>
        public FilterBarControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Wird ausgelöst, wenn ein Filter ein- oder ausgeschaltet wurde.
        /// </summary>
        public event EventHandler<FilterToggledEventArgs>? FilterToggled;

        /// <summary>Die angebotenen Filter.</summary>
        public static readonly DependencyProperty ChipsProperty =
            DependencyProperty.Register(nameof(Chips), typeof(IReadOnlyList<FilterChip>), typeof(FilterBarControl),
                new PropertyMetadata(null, (d, e) => ((FilterBarControl)d).ChipItems.ItemsSource = e.NewValue));

        /// <summary>Die angebotenen Filter.</summary>
        public IReadOnlyList<FilterChip>? Chips
        {
            get => (IReadOnlyList<FilterChip>?)GetValue(ChipsProperty);
            set => SetValue(ChipsProperty, value);
        }

        private void OnChipClick(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton { DataContext: FilterChip chip })
            {
                FilterToggled?.Invoke(this, new FilterToggledEventArgs(chip));
            }
        }
    }
}
