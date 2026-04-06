using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Definiert den Vertrag für das Anzeigen von Fehler-Dialogen.
    /// Kapselt die WinUI-3-ContentDialog-Logik und entkoppelt sie von den ViewModels.
    /// </summary>
    public interface IErrorDialogService
    {
        /// <summary>
        /// Zeigt einen modalen Fehler-Dialog mit Titel und Nachricht.
        /// Muss auf dem UI-Thread aufgerufen werden.
        /// </summary>
        /// <param name="title">Titel des Dialogs.</param>
        /// <param name="message">Fehlermeldung für den Benutzer.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        Task ShowAsync(string title, string message);
    }
}
