namespace EchoPlay.Data.Replication
{
    /// <summary>
    /// Am Ziel ist weniger Platz frei, als ein Vorgang braucht.
    /// </summary>
    /// <remarks>
    /// Die Meldung nennt beide Zahlen, damit der Nutzer weiß, wie viel er frei machen muss, statt
    /// es nach einem halb geschriebenen Paket herauszufinden.
    /// </remarks>
    public sealed class InsufficientSpaceException : IOException
    {
        /// <summary>Erzeugt die Ausnahme ohne Zahlen.</summary>
        public InsufficientSpaceException()
        {
        }

        /// <summary>Erzeugt die Ausnahme mit Text.</summary>
        /// <param name="message">Der Text.</param>
        public InsufficientSpaceException(string message)
            : base(message)
        {
        }

        /// <summary>Erzeugt die Ausnahme mit Text und Ursache.</summary>
        /// <param name="message">Der Text.</param>
        /// <param name="innerException">Die Ursache.</param>
        public InsufficientSpaceException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>Erzeugt die Ausnahme mit den beiden Zahlen.</summary>
        /// <param name="requiredBytes">Benötigter Platz.</param>
        /// <param name="availableBytes">Freier Platz.</param>
        public InsufficientSpaceException(long requiredBytes, long availableBytes)
            : base($"Es werden {requiredBytes} Byte gebraucht, frei sind {availableBytes}.")
        {
            RequiredBytes = requiredBytes;
            AvailableBytes = availableBytes;
        }

        /// <summary>Benötigter Platz in Byte.</summary>
        public long RequiredBytes { get; }

        /// <summary>Freier Platz in Byte.</summary>
        public long AvailableBytes { get; }
    }

    /// <summary>
    /// Prüft den freien Platz auf dem Laufwerk eines Verzeichnisses.
    /// </summary>
    internal static class SpaceCheck
    {
        /// <summary>
        /// Wirft, wenn auf dem Laufwerk von <paramref name="directory"/> weniger als
        /// <paramref name="requiredBytes"/> frei sind.
        /// </summary>
        /// <param name="directory">Ein Verzeichnis auf dem Ziel-Laufwerk.</param>
        /// <param name="requiredBytes">Benötigter Platz.</param>
        /// <exception cref="InsufficientSpaceException">Es ist zu wenig frei.</exception>
        public static void Require(string directory, long requiredBytes)
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(directory));
            if (string.IsNullOrEmpty(root))
            {
                return;
            }

            long available;
            try
            {
                available = new DriveInfo(root).AvailableFreeSpace;
            }
            catch (ArgumentException)
            {
                // Netzfreigaben (\\server\freigabe) kennt DriveInfo nicht. Dort lässt sich der Platz
                // nicht vorab bestimmen; ein Mangel zeigt sich dann beim Schreiben, und die
                // Zwischendatei wird weggeräumt.
                return;
            }

            if (available < requiredBytes)
            {
                throw new InsufficientSpaceException(requiredBytes, available);
            }
        }
    }
}
