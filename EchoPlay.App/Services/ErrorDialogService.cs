using EchoPlay.App.Helpers;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Zeigt Fehler-Dialoge über den WinUI-3-ContentDialog an.
    /// Kapselt die Abhängigkeit zu <see cref="ContentDialog"/> und
    /// <see cref="App.MainWindow"/>, damit ViewModels plattformunabhängig bleiben.
    /// </summary>

    public sealed class ErrorDialogService : IErrorDialogService
    {
        /// <summary>
        /// Zeigt einen modalen Fehler-Dialog mit Titel und Nachricht.
        /// Muss auf dem UI-Thread aufgerufen werden, da <see cref="ContentDialog"/>
        /// ein XamlRoot aus dem aktiven Fenster benötigt.
        /// </summary>
        /// <param name="title">Titel des Dialogs.</param>
        /// <param name="message">Fehlermeldung für den Benutzer.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        public async Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            ContentDialog dialog = new()
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = App.MainWindow!.Content.XamlRoot
            };

            ContentDialogDragHelper.MakeDraggable(dialog);
            _ = await dialog.ShowAsync();
        }
    }
}
