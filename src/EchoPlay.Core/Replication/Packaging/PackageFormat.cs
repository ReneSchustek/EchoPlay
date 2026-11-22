namespace EchoPlay.Core.Replication.Packaging
{
    /// <summary>
    /// Feste Größen des Paketformats für Umzug und Abgleich.
    /// </summary>
    /// <remarks>
    /// Ein Paket ist ein ZIP-Archiv mit genau diesen Einträgen. Die Grenzen schützen beim
    /// Einspielen vor einem präparierten Archiv: zu viele Einträge, ein riesiges Manifest oder ein
    /// Eintrag, der beim Entpacken um ein Vielfaches wächst.
    /// </remarks>
    public static class PackageFormat
    {
        /// <summary>Aktuelle Version des Formats.</summary>
        public const int CurrentFormatVersion = 1;

        /// <summary>Dateiendung eines Pakets.</summary>
        public const string FileExtension = ".echoplay";

        /// <summary>Das Manifest.</summary>
        public const string ManifestEntry = "manifest.json";

        /// <summary>Die Datenbank als SQLite-Datei.</summary>
        public const string DatabaseEntry = "database.sqlite";

        /// <summary>Die Zugangsdaten, mit einem Kennwort verschlüsselt; nur wenn beim Export gewählt.</summary>
        public const string CredentialsEntry = "credentials.sealed";

        /// <summary>Höchstzahl der Einträge im Archiv, Manifest eingeschlossen.</summary>
        public const int MaxEntries = 3;

        /// <summary>Höchstlänge des Manifests in Zeichen.</summary>
        public const int MaxManifestLength = 64 * 1024;

        /// <summary>Höchstlänge der verschlüsselten Zugangsdaten in Byte.</summary>
        public const int MaxCredentialsLength = 64 * 1024;

        /// <summary>
        /// Höchstgröße der entpackten Datenbank. Die Produktivdatei liegt bei 1,35 GB; das
        /// Zwanzigfache lässt Raum für Wachstum und schließt trotzdem eine Archivbombe aus.
        /// </summary>
        public const long MaxDatabaseLength = 32L * 1024 * 1024 * 1024;

        /// <summary>
        /// Höchstes Verhältnis entpackt zu gepackt für Einträge über einem Megabyte. Eine Datenbank
        /// mit Cover-Bildern packt sich kaum; Text wird etwa ein Zehntel so groß. Ein Verhältnis über
        /// 200 hat kein echtes Paket, aber jede Archivbombe.
        /// </summary>
        public const double MaxCompressionRatio = 200;

        /// <summary>Ob <paramref name="name"/> ein erlaubter Inhalt neben dem Manifest ist.</summary>
        /// <param name="name">Name im Archiv.</param>
        /// <returns><see langword="true"/> für Datenbank und Zugangsdaten.</returns>
        public static bool IsContentEntry(string name) => name is DatabaseEntry or CredentialsEntry;
    }
}
