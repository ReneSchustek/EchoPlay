using System.IO.Compression;

namespace EchoPlay.Data.Replication
{
    /// <summary>
    /// Holt einen Eintrag, den ein Paket haben muss. Die Strukturprüfung stellt das vorher sicher;
    /// fehlt er trotzdem, wird das Paket mit Grund abgelehnt statt mit einem Nullverweis abzubrechen.
    /// </summary>
    internal static class RequiredEntry
    {
        /// <summary>Liefert den Eintrag oder lehnt das Paket ab.</summary>
        /// <param name="archive">Das geöffnete Paket.</param>
        /// <param name="name">Der Name des Eintrags im Paket.</param>
        /// <returns>Der Eintrag.</returns>
        /// <exception cref="PackageRejectedException">Der Eintrag fehlt.</exception>
        public static ZipArchiveEntry Of(ZipArchive archive, string name)
            => archive.GetEntry(name) ?? throw new PackageRejectedException($"Im Paket fehlt der Eintrag „{name}“.");
    }
}
