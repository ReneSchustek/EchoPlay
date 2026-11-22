namespace EchoPlay.Data.Context
{
    /// <summary>
    /// Kennt den Speicherort der SQLite-Datenbankdatei im lokalen Anwendungsdaten-Verzeichnis
    /// des Windows-Benutzers (<c>%LOCALAPPDATA%\EchoPlay\echoplay.db</c>).
    /// </summary>
    public static class DatabasePathProvider
    {
        private const string AppFolderName = "EchoPlay";
        private const string DatabaseFileName = "echoplay.db";

        /// <summary>
        /// Ermittelt den absoluten Pfad zur Datenbankdatei und legt das Verzeichnis an, falls es
        /// noch fehlt.
        /// </summary>
        /// <returns>Der vollständige Pfad zur Datei <c>echoplay.db</c>.</returns>
        public static string PrepareDatabasePath()
        {
            string appFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName);

            // SQLite legt die Datei beim ersten Öffnen selbst an, das Verzeichnis aber nicht;
            // ohne diesen Schritt scheitert der erste Verbindungsaufbau einer Neuinstallation.
            _ = Directory.CreateDirectory(appFolder);
            return Path.Combine(appFolder, DatabaseFileName);
        }
    }
}
