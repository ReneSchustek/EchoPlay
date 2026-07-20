using EchoPlay.App.Helpers;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Reine Datenstruktur für einen Bestätigungs-Dialog. Enthält die vier
    /// Texte, die der WinUI-Renderer in den ContentDialog übernimmt — der
    /// Aufbau dieser Texte (inkl. lokalisierter Default-Buttons) bleibt
    /// dadurch ohne XamlRoot testbar.
    /// </summary>
    /// <param name="Title">Titel des Dialogs.</param>
    /// <param name="Message">Die eigentliche Frage/Erklärung.</param>
    /// <param name="PrimaryButtonText">Beschriftung der Bestätigen-Schaltflaeche (Default: "CommonYes"-Resource).</param>
    /// <param name="CloseButtonText">Beschriftung der Abbrechen-Schaltflaeche (Default: "CommonCancel"-Resource).</param>
    public sealed record ConfirmationDialogContent(
        string Title,
        string Message,
        string PrimaryButtonText,
        string CloseButtonText)
    {
        /// <summary>
        /// Baut den Dialog-Content mit den lokalisierten Default-Buttons aus den
        /// Strings/-Resourcen. Wird vom Service vor dem Rendern aufgerufen,
        /// bleibt ohne WinUI-Kontext nutzbar.
        /// </summary>
        public static ConfirmationDialogContent Build(string title, string message)
        {
            // Fallback-Texte schützen vor leeren resw-Einträgen oder Test-Hosts
            // ohne WinUI-Runtime (siehe SafeResourceLoader-Guard).
            string yes = SafeResourceLoader.Get("CommonYes", "Ja");
            string cancel = SafeResourceLoader.Get("CommonCancel", "Abbrechen");
            return new(title, message, yes, cancel);
        }
    }
}
