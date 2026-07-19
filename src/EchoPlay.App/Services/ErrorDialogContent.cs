namespace EchoPlay.App.Services
{
    /// <summary>
    /// Reine Datenstruktur fuer einen Fehler-Dialog. Trennt den Daten-Aufbau
    /// vom WinUI-Rendering, damit Tests den Content ohne XamlRoot pruefen koennen.
    /// </summary>
    /// <param name="Title">Titel des Dialogs.</param>
    /// <param name="Message">Fehlermeldung fuer den Benutzer.</param>
    /// <param name="CloseButtonText">Beschriftung der Schliessen-Schaltflaeche (Default: "OK").</param>
    public sealed record ErrorDialogContent(
        string Title,
        string Message,
        string CloseButtonText = "OK")
    {
        /// <summary>
        /// Baut den Dialog-Content mit Default-Close-Text "OK".
        /// </summary>
        public static ErrorDialogContent Build(string title, string message)
            => new(title, message);
    }
}
