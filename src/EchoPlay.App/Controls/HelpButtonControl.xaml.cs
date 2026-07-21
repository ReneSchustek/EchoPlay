using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EchoPlay.App.Controls
{
    /// <summary>
    /// Wiederverwendbarer Hilfe-Button mit gekoppeltem TeachingTip.
    /// Ersetzt das frühere Muster aus inline-Button + inline-TeachingTip + OnHelpClick-Handler,
    /// das in sieben Hauptseiten dupliziert war. Der Aufrufer setzt lediglich
    /// <see cref="TipUid"/>; der lokalisierte Untertitel wird daraus über die Ressourcen
    /// (Schlüssel <c>{TipUid}.Subtitle</c>) automatisch geladen.
    /// </summary>
    public sealed partial class HelpButtonControl : UserControl
    {
        /// <summary>
        /// Initialisiert das Control.
        /// </summary>
        public HelpButtonControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Ressourcen-Schlüssel für den TeachingTip-Untertitel. Zum Schlüsselwert wird
        /// <c>.Subtitle</c> angehängt, um den lokalisierten Text zu laden.
        /// </summary>
        public static readonly DependencyProperty TipUidProperty =
            DependencyProperty.Register(
                nameof(TipUid),
                typeof(string),
                typeof(HelpButtonControl),
                new PropertyMetadata(string.Empty, OnTipUidChanged));

        /// <summary>Ressourcen-Schlüssel für den TeachingTip-Untertitel.</summary>
        public string TipUid
        {
            get => (string)GetValue(TipUidProperty);
            set => SetValue(TipUidProperty, value);
        }

        private static void OnTipUidChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            HelpButtonControl control = (HelpButtonControl)d;
            string uid = (string)e.NewValue;

            if (string.IsNullOrEmpty(uid))
            {
                control.HelpTip.Subtitle = string.Empty;
                return;
            }

            control.HelpTip.Subtitle = EchoPlay.App.Helpers.SafeResourceLoader.Get($"{uid}/Subtitle");
        }

        private void OnHelpClick(object sender, RoutedEventArgs e)
        {
            HelpTip.IsOpen = true;
        }
    }
}
