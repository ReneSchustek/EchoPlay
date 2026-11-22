namespace EchoPlay.Data.Replication
{
    /// <summary>
    /// Pfade, die Export, Einspielen und Mediathek-Archiv von der Oberfläche bekommen. Ein relativer
    /// Pfad hinge am Arbeitsverzeichnis des Prozesses und landete beim nächsten Start woanders.
    /// </summary>
    internal static class AbsolutePath
    {
        /// <summary>
        /// Verlangt einen absoluten Pfad und gibt ihn vollständig aufgelöst und ohne Trenner am Ende zurück.
        /// </summary>
        /// <param name="path">Der Pfad.</param>
        /// <param name="parameterName">Name des Parameters für die Meldung.</param>
        /// <returns>Der aufgelöste Pfad.</returns>
        /// <exception cref="ArgumentException">Der Pfad ist leer oder nicht absolut.</exception>
        public static string Require(string path, string parameterName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
            return Path.IsPathFullyQualified(path)
                ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))
                : throw new ArgumentException("Der Pfad muss absolut sein.", parameterName);
        }
    }
}
