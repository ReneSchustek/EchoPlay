using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Windows.Input;

namespace EchoPlay.App.Controls
{
    /// <summary>
    /// Wiederverwendbarer Leer-Zustand mit Icon, Hinweistext und optionalem Aktions-Button.
    /// Wird überall dort eingesetzt, wo "Keine Daten"-Zustände angezeigt werden:
    /// Online-Mediathek (kein Provider, keine Serien), lokale Mediathek (kein Scan) etc.
    /// </summary>
    public sealed partial class EmptyStatePanel : UserControl
    {
        /// <summary>
        /// Initialisiert das Panel.
        /// </summary>
        public EmptyStatePanel()
        {
            InitializeComponent();
        }

        /// <summary>Glyph des Icons (z.B. Lupe, Warnung, Info).</summary>
        public static readonly DependencyProperty GlyphProperty =
            DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(EmptyStatePanel),
                new PropertyMetadata("\uE8D6", (d, e) => ((EmptyStatePanel)d).IconElement.Glyph = (string)e.NewValue));

        /// <summary>Glyph des Icons.</summary>
        public string Glyph
        {
            get => (string)GetValue(GlyphProperty);
            set => SetValue(GlyphProperty, value);
        }

        /// <summary>Hinweistext unter dem Icon.</summary>
        public static readonly DependencyProperty MessageProperty =
            DependencyProperty.Register(nameof(Message), typeof(string), typeof(EmptyStatePanel),
                new PropertyMetadata(string.Empty, (d, e) => ((EmptyStatePanel)d).MessageText.Text = (string)e.NewValue));

        /// <summary>Hinweistext.</summary>
        public string Message
        {
            get => (string)GetValue(MessageProperty);
            set => SetValue(MessageProperty, value);
        }

        /// <summary>Text des Aktions-Buttons. Leer = Button unsichtbar.</summary>
        public static readonly DependencyProperty ActionTextProperty =
            DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(EmptyStatePanel),
                new PropertyMetadata(null, OnActionTextChanged));

        /// <summary>Text des Aktions-Buttons.</summary>
        public string? ActionText
        {
            get => (string?)GetValue(ActionTextProperty);
            set => SetValue(ActionTextProperty, value);
        }

        private static void OnActionTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            EmptyStatePanel panel = (EmptyStatePanel)d;
            string? text = (string?)e.NewValue;
            panel.ActionButton.Content = text;
            panel.ActionButton.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Command des Aktions-Buttons.</summary>
        public static readonly DependencyProperty ActionCommandProperty =
            DependencyProperty.Register(nameof(ActionCommand), typeof(ICommand), typeof(EmptyStatePanel),
                new PropertyMetadata(null, (d, e) => ((EmptyStatePanel)d).ActionButton.Command = (ICommand?)e.NewValue));

        /// <summary>Command des Aktions-Buttons.</summary>
        public ICommand? ActionCommand
        {
            get => (ICommand?)GetValue(ActionCommandProperty);
            set => SetValue(ActionCommandProperty, value);
        }
    }
}
