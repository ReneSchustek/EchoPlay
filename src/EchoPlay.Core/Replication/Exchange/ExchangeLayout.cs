using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace EchoPlay.Core.Replication.Exchange
{
    /// <summary>Eine Protokolldatei im gemeinsamen Ordner, aus ihrem Namen gelesen.</summary>
    /// <param name="Sequence">Laufende Nummer.</param>
    /// <param name="IsSnapshot">Ob es ein Gesamtstand ist.</param>
    /// <param name="Path">Der volle Pfad.</param>
    public sealed record ExchangeFile(long Sequence, bool IsSnapshot, string Path);

    /// <summary>
    /// Aufbau des gemeinsamen Ordners: <c>changes/&lt;Geräte-Kennung&gt;/&lt;Nummer&gt;.json</c> für
    /// Änderungen und <c>&lt;Nummer&gt;.snapshot.json</c> für Gesamtstände. Jedes Gerät schreibt nur
    /// in seinen eigenen Ordner; zwei Geräte schreiben nie dieselbe Datei, und ein Cloud-Dienst
    /// erzeugt keine Konfliktkopien.
    /// </summary>
    public static class ExchangeLayout
    {
        /// <summary>Unterordner aller Protokolle im gemeinsamen Ordner.</summary>
        public const string ChangesFolder = "changes";

        /// <summary>Endung einer Änderungsdatei.</summary>
        public const string ChangesSuffix = ".json";

        /// <summary>Endung eines Gesamtstands.</summary>
        public const string SnapshotSuffix = ".snapshot.json";

        /// <summary>Endung einer Datei, die gerade geschrieben wird; Leser übergehen sie.</summary>
        public const string PartialSuffix = ".tmp";

        /// <summary>Stellen der laufenden Nummer im Dateinamen; so sortieren die Namen wie die Nummern.</summary>
        private const int SequenceDigits = 12;

        /// <summary>Der Ordner eines Geräts.</summary>
        /// <param name="exchangeRoot">Der gemeinsame Ordner.</param>
        /// <param name="device">Das Gerät.</param>
        /// <returns>Der Pfad.</returns>
        public static string DeviceFolder(string exchangeRoot, Guid device)
            => Path.Combine(exchangeRoot, ChangesFolder, device.ToString("N"));

        /// <summary>Der Dateiname einer Protokolldatei.</summary>
        /// <param name="sequence">Laufende Nummer.</param>
        /// <param name="isSnapshot">Ob es ein Gesamtstand ist.</param>
        /// <returns>Der Name ohne Ordner.</returns>
        public static string FileName(long sequence, bool isSnapshot)
            => sequence.ToString("D" + SequenceDigits, CultureInfo.InvariantCulture) + (isSnapshot ? SnapshotSuffix : ChangesSuffix);

        /// <summary>
        /// Liest Nummer und Art aus einem Dateinamen. Fremde Dateien und solche, die gerade
        /// geschrieben werden, gehören nicht zum Protokoll.
        /// </summary>
        /// <param name="path">Der Pfad.</param>
        /// <param name="file">Die erkannte Datei.</param>
        /// <returns><see langword="true"/>, wenn der Name zum Protokoll gehört.</returns>
        public static bool TryRead(string path, [NotNullWhen(true)] out ExchangeFile? file)
        {
            ArgumentNullException.ThrowIfNull(path);
            file = null;
            string name = System.IO.Path.GetFileName(path);
            bool isSnapshot = name.EndsWith(SnapshotSuffix, StringComparison.Ordinal);
            string number = isSnapshot
                ? name[..^SnapshotSuffix.Length]
                : name.EndsWith(ChangesSuffix, StringComparison.Ordinal) ? name[..^ChangesSuffix.Length] : string.Empty;

            if (number.Length != SequenceDigits || !number.All(char.IsAsciiDigit)
                || !long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out long sequence) || sequence < 1)
            {
                return false;
            }

            file = new ExchangeFile(sequence, isSnapshot, path);
            return true;
        }

        /// <summary>
        /// Wählt aus den Dateien eines Geräts, was nach Nummer <paramref name="seen"/> noch zu lesen ist:
        /// der jüngste Gesamtstand nach dieser Nummer, falls es einen gibt, und danach die Änderungen ohne
        /// Lücke. Endet die Reihe an einer Lücke, fehlt eine Datei noch (sie wird vielleicht gerade
        /// übertragen); der Rest wartet bis zum nächsten Lauf.
        /// </summary>
        /// <param name="files">Die Dateien des Geräts.</param>
        /// <param name="seen">Die höchste schon übernommene Nummer.</param>
        /// <returns>Die Dateien in Lesereihenfolge.</returns>
        public static IReadOnlyList<ExchangeFile> PendingAfter(IEnumerable<ExchangeFile> files, long seen)
        {
            ArgumentNullException.ThrowIfNull(files);
            List<ExchangeFile> all = [.. files.OrderBy(f => f.Sequence).ThenBy(f => f.IsSnapshot)];
            List<ExchangeFile> pending = [];
            long next = seen + 1;

            ExchangeFile? snapshot = all.LastOrDefault(f => f.IsSnapshot && f.Sequence >= next);
            if (snapshot is not null)
            {
                pending.Add(snapshot);
                next = snapshot.Sequence + 1;
            }

            foreach (ExchangeFile change in all.Where(f => !f.IsSnapshot && f.Sequence >= next))
            {
                if (change.Sequence != next)
                {
                    break;
                }

                pending.Add(change);
                next++;
            }

            return pending;
        }
    }
}
