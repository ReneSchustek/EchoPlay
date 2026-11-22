namespace EchoPlay.Core.Replication.Packaging
{
    /// <summary>
    /// Feste Größen des Mediathek-Archivs: die lokale Mediathek samt Ordnerstruktur und dem
    /// Datenpaket in einer Datei, für den Umzug auf einen anderen Rechner.
    /// </summary>
    /// <remarks>
    /// Ein ZIP-Archiv mit dem Manifest, dem Datenpaket (<see cref="DataPackageEntry"/>) und je Datei
    /// und Ordner der Mediathek einem Eintrag unter <see cref="MediaPrefix"/>. Die Audiodateien sind
    /// schon komprimiert und werden nur gespeichert. Die Grenzen schützen beim Auspacken vor einem
    /// präparierten Archiv.
    /// </remarks>
    public static class LibraryArchiveFormat
    {
        /// <summary>Aktuelle Version des Formats.</summary>
        public const int CurrentFormatVersion = 1;

        /// <summary>Dateiendung eines Mediathek-Archivs.</summary>
        public const string FileExtension = ".echoplay-mediathek";

        /// <summary>Das Manifest.</summary>
        public const string ManifestEntry = "manifest.json";

        /// <summary>Das Datenpaket aus Export und Einspielen, unverändert eingebettet.</summary>
        public const string DataPackageEntry = "data" + PackageFormat.FileExtension;

        /// <summary>Vorsilbe aller Einträge der Mediathek; dahinter steht der relative Pfad.</summary>
        public const string MediaPrefix = "media/";

        /// <summary>
        /// Höchstzahl der Dateien. Der Produktivbestand hat gut 40.000 Spuren; das Zehnfache lässt Raum
        /// und begrenzt trotzdem das Manifest und das Inhaltsverzeichnis im Speicher.
        /// </summary>
        public const int MaxFiles = 500_000;

        /// <summary>Höchstzahl der Ordner, aus demselben Grund wie <see cref="MaxFiles"/>.</summary>
        public const int MaxDirectories = 500_000;

        /// <summary>Höchstlänge des Manifests in Zeichen; eine Datei braucht darin gut 200.</summary>
        public const int MaxManifestLength = 128 * 1024 * 1024;

        /// <summary>Höchstgröße einer einzelnen Datei der Mediathek.</summary>
        public const long MaxFileLength = 64L * 1024 * 1024 * 1024;

        /// <summary>Höchstlänge eines relativen Pfads in Zeichen.</summary>
        public const int MaxRelativePathLength = 1024;

        /// <summary>Höchstgröße des eingebetteten Datenpakets: die Datenbank und ein Rest für den Rahmen.</summary>
        public const long MaxDataPackageLength = PackageFormat.MaxDatabaseLength + (64L * 1024 * 1024);

        /// <summary>Der Name des Archiveintrags für einen relativen Dateipfad.</summary>
        /// <param name="relativePath">Der relative Pfad mit <c>/</c> als Trenner.</param>
        /// <returns>Der Eintragsname.</returns>
        public static string FileEntryName(string relativePath) => MediaPrefix + relativePath;

        /// <summary>Der Name des Archiveintrags für einen relativen Ordnerpfad; endet auf <c>/</c>.</summary>
        /// <param name="relativePath">Der relative Pfad mit <c>/</c> als Trenner.</param>
        /// <returns>Der Eintragsname.</returns>
        public static string DirectoryEntryName(string relativePath) => MediaPrefix + relativePath + "/";
    }
}
