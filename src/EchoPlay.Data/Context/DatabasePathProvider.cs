namespace EchoPlay.Data.Context
{
    /// <summary>
    /// Berechnet den Speicherort der SQLite-Datenbankdatei basierend auf dem Systempfad.
    /// </summary>
    public static class DatabasePathProvider
    {
        /// <summary>
        /// Ermittelt den absoluten Dateipfad zur Datenbank im lokalen Anwendungsdaten-Verzeichnis.
        /// </summary>
        /// <returns>Der vollständige Pfad zur echoplay.db Datei.</returns>
        public static string GetDatabasePath()
        {
            string folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(folder, "EchoPlay");

            // Wir stellen sicher, dass das Verzeichnis existiert, damit EF Core beim 
            // Verbindungsaufbau keine "DirectoryNotFound"-Exception wirft.
            if (!Directory.Exists(appFolder)) _ = Directory.CreateDirectory(appFolder);
            return Path.Combine(appFolder, "echoplay.db");
        }
    }
}
