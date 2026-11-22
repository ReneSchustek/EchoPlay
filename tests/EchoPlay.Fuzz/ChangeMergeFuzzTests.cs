using EchoPlay.Core.Replication;
using EchoPlay.Core.Replication.Exchange;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace EchoPlay.Fuzz
{
    /// <summary>
    /// Eigenschaftstests für das Zusammenführen beim Abgleich. Reihenfolge und doppeltes Einlesen
    /// dürfen das Ergebnis nie ändern; sonst liefen zwei Geräte auseinander.
    /// </summary>
    public sealed class ChangeMergeFuzzTests
    {
        private static readonly Guid[] Devices = [new("11111111-1111-1111-1111-111111111111"), new("22222222-2222-2222-2222-222222222222"), new("33333333-3333-3333-3333-333333333333")];

        /// <summary>
        /// Stände eines Datensatzes. Der Zeitpunkt bestimmt den Inhalt eindeutig, wie bei echten
        /// Geräten: Zwei Schreibvorgänge bekommen nie denselben Zeitpunkt.
        /// </summary>
        private static Gen<ChangeRecord> Records(string kind) =>
            from wall in Gen.Choose(0, 6)
            from counter in Gen.Choose(0, 2)
            from device in Gen.Elements(Devices)
            select Build(kind, new HybridTimestamp(wall, counter, device));

        private static ChangeRecord Build(string kind, HybridTimestamp stamp)
        {
            // Inhalt aus dem Zeitpunkt abgeleitet, aber so gestreut, dass Felder einander widersprechen.
            // Kein Zufall: Jeder Zeitpunkt ergibt immer denselben Inhalt. Die Folge stammt aus einer
            // festen Rechenvorschrift, gesetzt aus dem Zeitpunkt.
            uint state = (uint)((stamp.WallMilliseconds * 31) + (stamp.Counter * 7) + stamp.DeviceId.GetHashCode());
            int Next(int range)
            {
                state = (state * 1664525) + 1013904223;
                return (int)((state >> 16) % (uint)range);
            }

            Dictionary<string, string?> fields = new(StringComparer.Ordinal)
            {
                ["Title"] = "Titel " + Next(3),
                [ChangeMerge.LastPositionField] = FieldText.Format(TimeSpan.FromSeconds(Next(4) * 30)),
                [ChangeMerge.LastPlayedAtField] = Next(3) == 0 ? null : FieldText.Format(new DateTime(2026, 11, 1 + Next(3), 0, 0, 0, DateTimeKind.Utc)),
                [ChangeMerge.IsCompletedField] = FieldText.Format(Next(2) == 0),
                [ChangeMerge.CompletedAtField] = Next(2) == 0 ? null : FieldText.Format(new DateTime(2026, 10, 1 + Next(3), 0, 0, 0, DateTimeKind.Utc)),
            };
            return new ChangeRecord(kind, "schlüssel", null, stamp, Next(4) == 0, fields);
        }

        private static Arbitrary<(ChangeRecord, ChangeRecord, ChangeRecord)> Triples(string kind) =>
            (from a in Records(kind) from b in Records(kind) from c in Records(kind) select (a, b, c)).ToArbitrary();

        [Property(MaxTest = 3000)]
        public Property Merge_IsCommutative_AssociativeAndIdempotent_ForEveryKind()
            => Prop.ForAll(Gen.Elements(ChangeKinds.PlaybackState, ChangeKinds.Series).ToArbitrary(), kind =>
                Prop.ForAll(Triples(kind), triple =>
                {
                    (ChangeRecord a, ChangeRecord b, ChangeRecord c) = triple;
                    return ChangeMerge.Merge(a, b).Equals(ChangeMerge.Merge(b, a))
                        && ChangeMerge.Merge(ChangeMerge.Merge(a, b), c).Equals(ChangeMerge.Merge(a, ChangeMerge.Merge(b, c)))
                        && ChangeMerge.Merge(a, a).Equals(a)
                        && ChangeMerge.Merge(ChangeMerge.Merge(a, b), b).Equals(ChangeMerge.Merge(a, b));
                }));

        [Property(MaxTest = 2000)]
        public Property Playback_OnceCompleted_StaysCompleted()
            => Prop.ForAll(Triples(ChangeKinds.PlaybackState), triple =>
            {
                (ChangeRecord a, ChangeRecord b, ChangeRecord c) = triple;
                bool anyCompleted = new[] { a, b, c }.Any(r => r.Field(ChangeMerge.IsCompletedField) == "true");
                ChangeRecord merged = ChangeMerge.MergeAll([a, b, c]);
                return (merged.Field(ChangeMerge.IsCompletedField) == "true") == anyCompleted;
            });

        [Property(MaxTest = 2000)]
        public Property Default_LatestStampWins()
            => Prop.ForAll(Triples(ChangeKinds.Series), triple =>
            {
                (ChangeRecord a, ChangeRecord b, ChangeRecord c) = triple;
                HybridTimestamp latest = new[] { a.Stamp, b.Stamp, c.Stamp }.Max();
                return ChangeMerge.MergeAll([a, b, c]).Stamp == latest;
            });

        [Property(MaxTest = 3000)]
        public bool Batch_AnyText_NeverThrows(string? json)
        {
            _ = ChangeBatch.TryParse(json, out _, out _);
            return true;
        }
    }
}
