using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Windows.Input;

namespace EchoPlay.App.Controls
{
    /// <summary>
    /// Seitenkopf der Gestaltungslinie: Titel, erklärende Zeile, Primäraktion rechts.
    /// <para>
    /// Als gemeinsamer Baustein, damit der bequeme Weg auch der richtige ist: Wer den
    /// Seitenkopf fertig vorfindet, baut ihn nicht auf jeder Seite neu — und damit jedes Mal
    /// ein wenig anders.
    /// </para>
    /// </summary>
    public sealed partial class PageHeaderControl : UserControl
    {
        /// <summary>
        /// Initialisiert den Seitenkopf.
        /// </summary>
        public PageHeaderControl()
        {
            InitializeComponent();
        }

        /// <summary>Titel der Seite.</summary>
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(PageHeaderControl),
                new PropertyMetadata(string.Empty, (d, e) => ((PageHeaderControl)d).TitleText.Text = (string)e.NewValue));

        /// <summary>Titel der Seite.</summary>
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        /// <summary>Zeile unter dem Titel. Leer = Zeile bleibt unsichtbar.</summary>
        public static readonly DependencyProperty SubtitleProperty =
            DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(PageHeaderControl),
                new PropertyMetadata(null, OnSubtitleChanged));

        /// <summary>Zeile unter dem Titel.</summary>
        public string? Subtitle
        {
            get => (string?)GetValue(SubtitleProperty);
            set => SetValue(SubtitleProperty, value);
        }

        private static void OnSubtitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            PageHeaderControl header = (PageHeaderControl)d;
            string? text = (string?)e.NewValue;
            header.SubtitleText.Text = text ?? string.Empty;
            header.SubtitleText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Beschriftung der Primäraktion. Leer = keine Schaltfläche.</summary>
        public static readonly DependencyProperty ActionTextProperty =
            DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(PageHeaderControl),
                new PropertyMetadata(null, OnActionTextChanged));

        /// <summary>Beschriftung der Primäraktion.</summary>
        public string? ActionText
        {
            get => (string?)GetValue(ActionTextProperty);
            set => SetValue(ActionTextProperty, value);
        }

        private static void OnActionTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            PageHeaderControl header = (PageHeaderControl)d;
            string? text = (string?)e.NewValue;
            header.PrimaryActionButton.Content = text;
            header.PrimaryActionButton.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Befehl der Primäraktion.</summary>
        public static readonly DependencyProperty ActionCommandProperty =
            DependencyProperty.Register(nameof(ActionCommand), typeof(ICommand), typeof(PageHeaderControl),
                new PropertyMetadata(null, (d, e) => ((PageHeaderControl)d).PrimaryActionButton.Command = (ICommand?)e.NewValue));

        /// <summary>Befehl der Primäraktion.</summary>
        public ICommand? ActionCommand
        {
            get => (ICommand?)GetValue(ActionCommandProperty);
            set => SetValue(ActionCommandProperty, value);
        }

        /// <summary>
        /// Wird beim Klick auf die Primäraktion ausgelöst.
        /// <para>
        /// Für Aktionen, die das Fenster brauchen — eine Ordnerauswahl etwa verlangt das
        /// Fensterhandle, das ein ViewModel nicht kennt. Wo ein Befehl genügt, ist
        /// <see cref="ActionCommand"/> der bessere Weg.
        /// </para>
        /// </summary>
        public event RoutedEventHandler? ActionClick;

        private void OnPrimaryActionClick(object sender, RoutedEventArgs e) =>
            ActionClick?.Invoke(this, e);
    }
}
