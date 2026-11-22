using System.Text;

namespace EchoPlay.Core.Replication.Exchange
{
    /// <summary>
    /// Der Stand eines fachlichen Datensatzes, wie ein Gerät ihn kennt: Art, Schlüssel, logischer
    /// Zeitpunkt der letzten Änderung, ob gelöscht, und die gemeinsamen Felder als Text.
    /// </summary>
    /// <remarks>
    /// Die Felder stehen als Text in invarianter Form (<see cref="FieldText"/>), nicht als
    /// .NET-Typen: Eine zweite Umsetzung auf einer anderen Plattform soll sie lesen können, ohne den
    /// C#-Code zu kennen. Zwei Sätze sind gleich, wenn alle Teile gleich sind, die Felder unabhängig
    /// von ihrer Reihenfolge.
    /// </remarks>
    /// <param name="Kind">Die Art, eine aus <see cref="ChangeKinds"/>.</param>
    /// <param name="Key">Der fachliche Schlüssel, auf allen Geräten gleich.</param>
    /// <param name="Parent">Schlüssel des übergeordneten Satzes (Serie einer Folge, Folge eines Hörstands), sonst leer.</param>
    /// <param name="Stamp">Logischer Zeitpunkt der letzten Änderung.</param>
    /// <param name="Deleted">Ob der Satz gelöscht ist (Grabstein).</param>
    /// <param name="Fields">Die gemeinsamen Felder, Name und Wert als Text.</param>
    public sealed record ChangeRecord(
        string Kind,
        string Key,
        string? Parent,
        HybridTimestamp Stamp,
        bool Deleted,
        IReadOnlyDictionary<string, string?> Fields)
    {
        /// <summary>Liest ein Feld; fehlt es, ist der Wert leer.</summary>
        /// <param name="name">Name des Felds.</param>
        /// <returns>Der Wert als Text, oder <see langword="null"/>.</returns>
        public string? Field(string name) => Fields.TryGetValue(name, out string? value) ? value : null;

        /// <summary>Eine Kopie mit geänderten Feldern und neuem Zeitpunkt.</summary>
        /// <param name="stamp">Der Zeitpunkt der Kopie.</param>
        /// <param name="changes">Die zu setzenden Felder.</param>
        /// <returns>Die Kopie.</returns>
        public ChangeRecord WithFields(HybridTimestamp stamp, IReadOnlyDictionary<string, string?> changes)
        {
            ArgumentNullException.ThrowIfNull(changes);
            Dictionary<string, string?> fields = new(Fields, StringComparer.Ordinal);
            foreach (KeyValuePair<string, string?> change in changes)
            {
                fields[change.Key] = change.Value;
            }

            return this with { Stamp = stamp, Fields = fields };
        }

        /// <summary>Vergleicht alle Teile, die Felder unabhängig von ihrer Reihenfolge.</summary>
        /// <param name="other">Der andere Satz.</param>
        /// <returns><see langword="true"/>, wenn beide denselben Stand beschreiben.</returns>
        public bool Equals(ChangeRecord? other) => other is not null && Canonical() == other.Canonical();

        /// <inheritdoc/>
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Canonical());

        /// <summary>
        /// Eine eindeutige Textform des ganzen Satzes, Felder nach Namen sortiert. Grundlage von
        /// Gleichheit und der festen Reihenfolge bei gleichem Zeitpunkt.
        /// </summary>
        /// <returns>Die Textform.</returns>
        internal string Canonical()
        {
            StringBuilder text = new();
            _ = text.Append(Kind).Append('\u001f').Append(Key).Append('\u001f').Append(Parent).Append('\u001f')
                .Append(Stamp.ToString()).Append('\u001f').Append(Deleted ? '1' : '0');
            foreach (KeyValuePair<string, string?> field in Fields.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                _ = text.Append('\u001e').Append(field.Key).Append('\u001f').Append(field.Value is null ? "\u0000" : field.Value);
            }

            return text.ToString();
        }
    }
}
