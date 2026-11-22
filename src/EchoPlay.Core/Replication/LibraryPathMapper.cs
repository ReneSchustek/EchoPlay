namespace EchoPlay.Core.Replication
{
    /// <summary>
    /// Rechnet Pfade der Mediathek zwischen der absoluten Form der Datenbank und einer Form relativ
    /// zum Stammpfad um, an einer Stelle für Export, Packen und Abgleich.
    /// </summary>
    /// <remarks>
    /// Die Datenbank speichert absolute Pfade, denn die Anwendung arbeitet mit ihnen auf genau
    /// einem Rechner. Verlässt ein Pfad den Rechner, taugt er nur relativ zum Stammpfad: Auf dem
    /// Zielrechner liegt die Mediathek woanders. Die relative Form trennt mit <c>/</c>, damit sie auch
    /// eine Anwendung auf einer anderen Plattform lesen kann.
    /// <para>
    /// Windows unterscheidet bei Pfaden keine Groß- und Kleinschreibung; der Vergleich mit dem
    /// Stammpfad tut es deshalb auch nicht. Pfade werden vor dem Vergleich aufgelöst, so dass
    /// <c>..</c> nicht aus dem Stamm herausführen kann, ohne dass es auffällt.
    /// </para>
    /// </remarks>
    public static class LibraryPathMapper
    {
        private const char PortableSeparator = '/';

        /// <summary>
        /// Rechnet einen absoluten Pfad in die Form relativ zum Stammpfad um.
        /// </summary>
        /// <param name="root">Der Stammpfad der Mediathek, absolut.</param>
        /// <param name="absolutePath">Der Pfad aus der Datenbank.</param>
        /// <returns>
        /// Der relative Pfad mit <c>/</c> als Trenner, oder <see langword="null"/>, wenn der Pfad nicht
        /// unter dem Stamm liegt. Solche Pfade bleiben absolut; der Aufrufer zählt sie.
        /// </returns>
        public static string? ToPortable(string root, string absolutePath)
        {
            if (!TryNormalizeRoot(root, out string normalizedRoot) || !TryFullPath(absolutePath, out string full))
            {
                return null;
            }

            if (!full.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string relative = full[normalizedRoot.Length..];
            return relative.Length == 0
                ? null
                : relative.Replace(Path.DirectorySeparatorChar, PortableSeparator);
        }

        /// <summary>
        /// Rechnet einen relativen Pfad aus <see cref="ToPortable"/> auf einen Stammpfad zurück.
        /// </summary>
        /// <param name="root">Der Stammpfad auf diesem Rechner, absolut.</param>
        /// <param name="portablePath">Der relative Pfad mit <c>/</c> als Trenner.</param>
        /// <returns>
        /// Der absolute Pfad, oder <see langword="null"/>, wenn der relative Pfad aus dem Stamm
        /// herausführt, absolut ist oder leer. Ein solcher Pfad kommt von außen und wird nicht befolgt.
        /// </returns>
        public static string? ToAbsolute(string root, string portablePath)
        {
            if (!TryNormalizeRoot(root, out string normalizedRoot) || string.IsNullOrWhiteSpace(portablePath))
            {
                return null;
            }

            string local = portablePath.Replace(PortableSeparator, Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(local))
            {
                return null;
            }

            if (!TryFullPath(Path.Combine(normalizedRoot, local), out string full))
            {
                return null;
            }

            return full.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) && full.Length > normalizedRoot.Length
                ? full
                : null;
        }

        /// <summary>
        /// Verschiebt einen absoluten Pfad vom alten auf den neuen Stammpfad.
        /// </summary>
        /// <param name="oldRoot">Der bisherige Stammpfad.</param>
        /// <param name="newRoot">Der neue Stammpfad.</param>
        /// <param name="absolutePath">Der Pfad aus der Datenbank.</param>
        /// <returns>Der Pfad unter dem neuen Stamm, oder <see langword="null"/>, wenn er nicht unter dem alten lag.</returns>
        public static string? Rebase(string oldRoot, string newRoot, string absolutePath)
        {
            string? portable = ToPortable(oldRoot, absolutePath);
            return portable is null ? null : ToAbsolute(newRoot, portable);
        }

        /// <summary>
        /// Löst den Stammpfad auf und hängt einen Trenner an, damit <c>D:\Hörspiele</c> nicht auch
        /// <c>D:\Hörspiele alt</c> einschließt.
        /// </summary>
        private static bool TryNormalizeRoot(string root, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) || !TryFullPath(root, out string full))
            {
                return false;
            }

            normalized = Path.EndsInDirectorySeparator(full) ? full : full + Path.DirectorySeparatorChar;
            return true;
        }

        private static bool TryFullPath(string path, out string full)
        {
            full = string.Empty;
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            {
                return false;
            }

            try
            {
                full = Path.GetFullPath(path);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }
    }
}
