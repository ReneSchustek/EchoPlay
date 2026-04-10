using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EchoPlay.App.Controls
{
    /// <summary>
    /// Wiederverwendbare Speichern/Abbrechen-Buttonleiste für die Einstellungen-Tabs.
    /// Reicht Klicks über die Events <see cref="SaveClicked"/> und <see cref="CancelClicked"/>
    /// an die aufrufende Page weiter, damit diese die jeweilige Tab-Logik ausführen kann.
    /// </summary>
    public sealed partial class SaveCancelBar : UserControl
    {
        /// <summary>
        /// Initialisiert die Buttonleiste.
        /// </summary>
        public SaveCancelBar()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Wird ausgelöst, wenn der Nutzer auf "Speichern" klickt.
        /// Die aufrufende Page entscheidet, welche Daten gespeichert werden.
        /// </summary>
        public event RoutedEventHandler? SaveClicked;

        /// <summary>
        /// Wird ausgelöst, wenn der Nutzer auf "Abbrechen" klickt.
        /// Die aufrufende Page entscheidet, wie der Tab zurückgesetzt wird.
        /// </summary>
        public event RoutedEventHandler? CancelClicked;

        private void OnSaveButtonClick(object sender, RoutedEventArgs e)
        {
            SaveClicked?.Invoke(this, e);
        }

        private void OnCancelButtonClick(object sender, RoutedEventArgs e)
        {
            CancelClicked?.Invoke(this, e);
        }
    }
}
