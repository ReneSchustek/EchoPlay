using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace EchoPlay.App.Controls
{
    /// <summary>
    /// Suchfeld der Gestaltungslinie: filtert beim Tippen, lässt sich mit
    /// <see cref="VirtualKey.Escape"/> und über ein Löschen-Zeichen leeren.
    /// <para>
    /// Der Platzhalter ist zugleich die Beschriftung für die Sprachausgabe — ein Feld, das
    /// nur ein Lupensymbol trägt, wird sonst als „TextBox" vorgelesen.
    /// </para>
    /// </summary>
    public sealed partial class SearchBoxControl : UserControl
    {
        private bool _isPointerOver;
        private bool _hasFocus;

        /// <summary>
        /// Initialisiert das Suchfeld.
        /// </summary>
        public SearchBoxControl()
        {
            InitializeComponent();
        }

        /// <summary>Eingegebener Suchtext. Wird beim Tippen aktualisiert.</summary>
        public static readonly DependencyProperty SearchTextProperty =
            DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(SearchBoxControl),
                new PropertyMetadata(string.Empty, OnSearchTextChanged));

        /// <summary>Eingegebener Suchtext.</summary>
        public string SearchText
        {
            get => (string)GetValue(SearchTextProperty);
            set => SetValue(SearchTextProperty, value);
        }

        private static void OnSearchTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            SearchBoxControl control = (SearchBoxControl)d;
            string text = (string?)e.NewValue ?? string.Empty;

            // Nur schreiben, wenn sich der Inhalt wirklich unterscheidet: Sonst setzt das
            // Zuweisen die Schreibmarke bei jedem Tastendruck an den Anfang zurück.
            if (!string.Equals(control.InputBox.Text, text, System.StringComparison.Ordinal))
            {
                control.InputBox.Text = text;
            }

            control.ClearButton.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// Platzhalter im leeren Feld. Sagt, wonach gesucht wird („Titel, Serie, Sprecher"),
        /// und dient zugleich als Name für die Sprachausgabe.
        /// </summary>
        public static readonly DependencyProperty PlaceholderTextProperty =
            DependencyProperty.Register(nameof(PlaceholderText), typeof(string), typeof(SearchBoxControl),
                new PropertyMetadata(string.Empty, OnPlaceholderTextChanged));

        /// <summary>Platzhalter im leeren Feld.</summary>
        public string PlaceholderText
        {
            get => (string)GetValue(PlaceholderTextProperty);
            set => SetValue(PlaceholderTextProperty, value);
        }

        private static void OnPlaceholderTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            SearchBoxControl control = (SearchBoxControl)d;
            string text = (string?)e.NewValue ?? string.Empty;
            control.InputBox.PlaceholderText = text;
            AutomationProperties.SetName(control.InputBox, text);
        }

        /// <summary>
        /// Wird ausgelöst, wenn die Eingabetaste im gefüllten Feld gedrückt wurde.
        /// <para>
        /// Für Seiten, die neben dem Filtern noch etwas anzustoßen haben — die Online-Mediathek
        /// sucht damit beim Anbieter weiter. Das Filtern selbst braucht das nicht: Es geschieht
        /// schon beim Tippen.
        /// </para>
        /// </summary>
        public event EventHandler<SearchSubmittedEventArgs>? Submitted;

        private void OnTextChanged(object sender, TextChangedEventArgs e) =>
            SearchText = InputBox.Text;

        // ── Zustände des Rahmens ────────────────────────────────────────────────
        //
        // Die innere TextBox ist rahmenlos, damit Lupe, Eingabe und Löschen-Zeichen als ein
        // Feld wirken. Damit verliert sie aber auch die Rückmeldung, die WinUI sonst selbst
        // zeichnet: Zeiger darüber und — wichtiger — wo die Tastatur gerade steht. Beides
        // holt der äußere Rahmen hier nach.

        private void OnFieldPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _isPointerOver = true;
            UpdateFieldState();
        }

        private void OnFieldPointerExited(object sender, PointerRoutedEventArgs e)
        {
            _isPointerOver = false;
            UpdateFieldState();
        }

        private void OnInputGotFocus(object sender, RoutedEventArgs e)
        {
            _hasFocus = true;
            UpdateFieldState();
        }

        private void OnInputLostFocus(object sender, RoutedEventArgs e)
        {
            _hasFocus = false;
            UpdateFieldState();
        }

        /// <summary>
        /// Setzt den Zustand des Rahmens. Der Fokus hat Vorrang vor dem Zeiger — sonst
        /// verschwände die Marke, sobald jemand mit der Maus daneben fährt.
        /// </summary>
        private void UpdateFieldState()
        {
            string zustand = _hasFocus ? "Focused" : _isPointerOver ? "PointerOver" : "Normal";
            _ = VisualStateManager.GoToState(this, zustand, true);
        }

        private void OnKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                // Ein leeres Feld abzuschicken hätte nichts zu suchen — und die Taste bleibt
                // frei für das, was das umgebende Fenster damit vorhat.
                if (SearchText.Length == 0)
                {
                    return;
                }

                Submitted?.Invoke(this, new SearchSubmittedEventArgs(SearchText));
                e.Handled = true;
                return;
            }

            if (e.Key != VirtualKey.Escape)
            {
                return;
            }

            // Nur behandeln, wenn wirklich etwas zu leeren war — sonst schluckt das Suchfeld
            // die Taste, mit der ein umgebender Dialog geschlossen werden soll.
            if (SearchText.Length == 0)
            {
                return;
            }

            Clear();
            e.Handled = true;
        }

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            Clear();

            // Nach dem Löschen bleibt die Schreibmarke im Feld: Wer die Suche verwirft, will
            // in aller Regel gleich neu tippen.
            _ = InputBox.Focus(FocusState.Programmatic);
        }

        private void Clear() => SearchText = string.Empty;
    }
}
