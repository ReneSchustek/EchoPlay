using EchoPlay.App.Helpers;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Reine Datenstruktur für einen Fehler-Dialog. Trennt den Daten-Aufbau
    /// vom WinUI-Rendering, damit Tests den Content ohne XamlRoot prüfen können.
    /// </summary>
    /// <param name="Title">Titel des Dialogs.</param>
    /// <param name="Message">Fehlermeldung für den Benutzer.</param>
    /// <param name="CloseButtonText">Beschriftung der Schließen-Schaltfläche.</param>
    public sealed record ErrorDialogContent(
        string Title,
        string Message,
        string CloseButtonText = "OK")
    {
        /// <summary>
        /// Baut den Dialog-Content mit der Schließen-Beschriftung aus den Ressourcen, derselben
        /// wie in den übrigen Dialogen.
        /// </summary>
        /// <param name="title">Titel des Dialogs.</param>
        /// <param name="message">Fehlermeldung für den Benutzer.</param>
        /// <returns>Der Inhalt des Dialogs.</returns>
        public static ErrorDialogContent Build(string title, string message)
            => new(title, message, SafeResourceLoader.Get("CommonCloseButton", "OK"));
    }
}
