using EchoPlay.App.Helpers;
using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Zeigt Bestätigungs-Dialoge über den WinUI-3-ContentDialog an.
    /// Gibt <c>true</c> zurück, wenn der Benutzer auf die primäre Schaltfläche geklickt hat.
    /// </summary>
    public sealed class ConfirmationDialogService : IConfirmationDialogService
    {
        /// <summary>
        /// Zeigt einen modalen Bestätigungs-Dialog mit „Ja" und „Abbrechen".
        /// Muss auf dem UI-Thread aufgerufen werden, da <see cref="ContentDialog"/>
        /// ein XamlRoot aus dem aktiven Fenster benötigt.
        /// </summary>
        /// <param name="title">Titel des Dialogs.</param>
        /// <param name="message">Die Frage oder Erklärung für den Benutzer.</param>
        /// <returns><c>true</c>, wenn der Benutzer „Ja" gewählt hat; sonst <c>false</c>.</returns>
        public async Task<bool> ConfirmAsync(string title, string message)
        {
            ContentDialog dialog = new()
            {
                Title = title,
                Content = message,
                PrimaryButtonText = "Ja",
                CloseButtonText = "Abbrechen",
                XamlRoot = App.MainWindow!.Content.XamlRoot
            };

            ContentDialogDragHelper.MakeDraggable(dialog);
            ContentDialogResult result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }
    }
}
