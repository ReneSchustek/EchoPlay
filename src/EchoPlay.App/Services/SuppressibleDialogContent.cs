using EchoPlay.App.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Baut den Inhalt eines ausblendbaren Dialogs: die Meldung und darunter das Häkchen
    /// „nicht wieder anzeigen".
    /// </summary>
    /// <remarks>
    /// Bewusst nur der Zusammenbau der Steuerelemente. Was die Texte sind, entscheiden
    /// <see cref="ConfirmationDialogContent"/> und <see cref="ErrorDialogContent"/>, und ob
    /// das Häkchen etwas bewirkt, entscheidet <see cref="DialogSuppressionDecision"/> — beide
    /// bleiben ohne Fensterkontext prüfbar.
    /// </remarks>
    internal sealed class SuppressibleDialogContent
    {
        private readonly CheckBox _suppressCheckBox;

        private SuppressibleDialogContent(FrameworkElement root, CheckBox suppressCheckBox)
        {
            Root = root;
            _suppressCheckBox = suppressCheckBox;
        }

        /// <summary>Das fertige Element für den Inhalt des Dialogs.</summary>
        public FrameworkElement Root { get; }

        /// <summary>Gibt an, ob der Nutzer das Häkchen gesetzt hat.</summary>
        public bool IsSuppressRequested => _suppressCheckBox.IsChecked == true;

        /// <summary>
        /// Baut den Inhalt für einen reinen Hinweis: Das Häkchen blendet ihn künftig aus.
        /// </summary>
        /// <param name="message">Die anzuzeigende Meldung.</param>
        /// <returns>Der Inhalt samt Häkchen.</returns>
        public static SuppressibleDialogContent ForHint(string message) =>
            Build(message, SafeResourceLoader.Get("DialogSuppressHintCheckBox", "Diesen Hinweis nicht mehr anzeigen"));

        /// <summary>
        /// Baut den Inhalt für eine Rückfrage: Das Häkchen führt die Aktion künftig sofort aus.
        /// </summary>
        /// <param name="message">Die anzuzeigende Frage.</param>
        /// <returns>Der Inhalt samt Häkchen.</returns>
        public static SuppressibleDialogContent ForConfirmation(string message) =>
            Build(message, SafeResourceLoader.Get("DialogSuppressConfirmCheckBox", "Nicht mehr nachfragen, künftig sofort ausführen"));

        private static SuppressibleDialogContent Build(string message, string checkBoxText)
        {
            CheckBox suppressCheckBox = new()
            {
                Content = checkBoxText,
                Margin = new Thickness(0, 4, 0, 0)
            };

            StackPanel panel = new()
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap
                    },
                    suppressCheckBox
                }
            };

            return new SuppressibleDialogContent(panel, suppressCheckBox);
        }
    }
}
