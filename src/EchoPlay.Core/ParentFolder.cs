namespace EchoPlay.Core
{
    /// <summary>
    /// Der Ordner, in dem eine Datei liegt. <see cref="Path.GetDirectoryName(string)"/> liefert für
    /// eine Laufwerkswurzel oder einen leeren Pfad <see langword="null"/>; für die Dateipfade, die
    /// EchoPlay selbst bildet, ist das ein Programmfehler und kein Fall, der still weiterläuft.
    /// </summary>
    public static class ParentFolder
    {
        /// <summary>Liefert den Ordner der Datei.</summary>
        /// <param name="filePath">Pfad einer Datei.</param>
        /// <returns>Der übergeordnete Ordner.</returns>
        /// <exception cref="ArgumentException">Der Pfad hat keinen übergeordneten Ordner.</exception>
        public static string Of(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            return Path.GetDirectoryName(filePath)
                ?? throw new ArgumentException($"Der Pfad '{filePath}' hat keinen übergeordneten Ordner.", nameof(filePath));
        }
    }
}
