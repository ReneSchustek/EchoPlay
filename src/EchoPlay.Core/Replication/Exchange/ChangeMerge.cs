namespace EchoPlay.Core.Replication.Exchange
{
    /// <summary>
    /// Führt zwei Stände desselben Datensatzes zusammen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Jede Regel ist kommutativ, assoziativ und idempotent: Gleich, in welcher Reihenfolge und wie oft
    /// die Geräte ihre Stände einlesen, am Ende steht überall dasselbe. Das gelingt, weil jedes Feld aus
    /// einer festen Ordnung über alle Stände gewählt wird und nie aus dem Verlauf.
    /// </para>
    /// <para>
    /// Grundregel: Der Stand mit dem jüngsten logischen Zeitpunkt gewinnt ganz, Löschung eingeschlossen.
    /// Der Hörstand weicht ab: Die Position kommt von der jüngsten Wiedergabe (eine bloße Änderung am
    /// Gehört-Merker soll sie nicht zurückdrehen), „gehört“ bleibt gehört, und als Abschlusszeitpunkt
    /// gilt der früheste.
    /// </para>
    /// <para>
    /// Annahme: Zwei Stände mit gleichem Zeitpunkt haben denselben Inhalt. Der Zeitpunkt trägt das Gerät
    /// und einen Zähler; zwei verschiedene Schreibvorgänge bekommen nie denselben.
    /// </para>
    /// </remarks>
    public static class ChangeMerge
    {
        /// <summary>Feld des Hörstands: zuletzt gehörte Position.</summary>
        public const string LastPositionField = "LastPosition";

        /// <summary>Feld des Hörstands: Zeitpunkt der letzten Wiedergabe.</summary>
        public const string LastPlayedAtField = "LastPlayedAt";

        /// <summary>Feld des Hörstands: ob die Folge gehört ist.</summary>
        public const string IsCompletedField = "IsCompleted";

        /// <summary>Feld des Hörstands: wann sie als gehört markiert wurde.</summary>
        public const string CompletedAtField = "CompletedAt";

        /// <summary>
        /// Führt zwei Stände desselben Datensatzes zusammen.
        /// </summary>
        /// <param name="left">Ein Stand.</param>
        /// <param name="right">Ein Stand mit derselben Art und demselben Schlüssel.</param>
        /// <returns>Der zusammengeführte Stand.</returns>
        /// <exception cref="ArgumentException">Art oder Schlüssel unterscheiden sich.</exception>
        public static ChangeRecord Merge(ChangeRecord left, ChangeRecord right)
        {
            ArgumentNullException.ThrowIfNull(left);
            ArgumentNullException.ThrowIfNull(right);
            if (!string.Equals(left.Kind, right.Kind, StringComparison.Ordinal) || !string.Equals(left.Key, right.Key, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Verschiedene Datensätze: {left.Kind}/{left.Key} und {right.Kind}/{right.Key}.", nameof(right));
            }

            return left.Kind == ChangeKinds.PlaybackState ? MergePlayback(left, right) : Latest(left, right);
        }

        /// <summary>Führt beliebig viele Stände zusammen; die Reihenfolge spielt keine Rolle.</summary>
        /// <param name="records">Stände desselben Datensatzes, mindestens einer.</param>
        /// <returns>Der zusammengeführte Stand.</returns>
        public static ChangeRecord MergeAll(IEnumerable<ChangeRecord> records)
        {
            ArgumentNullException.ThrowIfNull(records);
            return records.Aggregate(Merge);
        }

        /// <summary>Der jüngere Stand; bei gleichem Zeitpunkt entscheidet die Textform, damit das Ergebnis fest ist.</summary>
        private static ChangeRecord Latest(ChangeRecord left, ChangeRecord right)
        {
            int byStamp = left.Stamp.CompareTo(right.Stamp);
            if (byStamp != 0)
            {
                return byStamp > 0 ? left : right;
            }

            return string.CompareOrdinal(left.Canonical(), right.Canonical()) >= 0 ? left : right;
        }

        /// <summary>
        /// Hörstand: alles vom jüngeren Stand, die Position aber von der jüngsten Wiedergabe, „gehört“
        /// als Oder und der früheste Abschlusszeitpunkt.
        /// </summary>
        private static ChangeRecord MergePlayback(ChangeRecord left, ChangeRecord right)
        {
            ChangeRecord latest = Latest(left, right);
            ChangeRecord played = ComparePlayed(left, right) >= 0 ? left : right;
            bool completed = IsCompleted(left) || IsCompleted(right);

            Dictionary<string, string?> fields = new(latest.Fields, StringComparer.Ordinal)
            {
                [LastPositionField] = played.Field(LastPositionField),
                [LastPlayedAtField] = played.Field(LastPlayedAtField),
                [IsCompletedField] = FieldText.Format(completed),
                [CompletedAtField] = completed ? EarliestCompletion(left, right) : latest.Field(CompletedAtField),
            };
            return latest with { Fields = fields };
        }

        /// <summary>
        /// Ordnet zwei Stände nach ihrer Wiedergabe: erst nach dem Zeitpunkt, dann nach der Position.
        /// Beides sind Inhalte, keine Zeitpunkte der Stände, damit die Wahl assoziativ bleibt.
        /// </summary>
        private static int ComparePlayed(ChangeRecord left, ChangeRecord right)
        {
            int byTime = Nullable.Compare(ReadTime(left.Field(LastPlayedAtField)), ReadTime(right.Field(LastPlayedAtField)));
            if (byTime != 0)
            {
                return byTime;
            }

            int byPosition = Nullable.Compare(ReadSpan(left.Field(LastPositionField)), ReadSpan(right.Field(LastPositionField)));
            return byPosition != 0
                ? byPosition
                : string.CompareOrdinal(left.Field(LastPositionField) + "|" + left.Field(LastPlayedAtField), right.Field(LastPositionField) + "|" + right.Field(LastPlayedAtField));
        }

        private static bool IsCompleted(ChangeRecord record) => record.Field(IsCompletedField) == "true";

        /// <summary>Der früheste Abschluss unter den Ständen, die „gehört“ sagen.</summary>
        private static string? EarliestCompletion(ChangeRecord left, ChangeRecord right)
        {
            DateTime? earliest = null;
            string? text = null;
            foreach (ChangeRecord record in new[] { left, right }.Where(IsCompleted))
            {
                string? candidate = record.Field(CompletedAtField);
                DateTime? time = ReadTime(candidate);
                if (time is not null && (earliest is null || time < earliest
                    || (time == earliest && string.CompareOrdinal(candidate, text) < 0)))
                {
                    earliest = time;
                    text = candidate;
                }
            }

            return text;
        }

        private static DateTime? ReadTime(string? text)
            => FieldText.TryParse(text, typeof(DateTime?), out object? value) ? (DateTime?)value : null;

        private static TimeSpan? ReadSpan(string? text)
            => FieldText.TryParse(text, typeof(TimeSpan?), out object? value) ? (TimeSpan?)value : null;
    }
}
