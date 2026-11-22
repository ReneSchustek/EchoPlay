using EchoPlay.Core.Replication;
using EchoPlay.Core.Replication.Exchange;
using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Logger.Abstractions;

namespace EchoPlay.Data.Replication.Exchange
{
    /// <summary>
    /// Übernimmt fremde Sätze in den Bestand: je Datensatz mit dem eigenen Stand zusammengeführt,
    /// geschrieben nur, wenn sich etwas ändert, und jede Übernahme mit Gerät, Zeitpunkt, altem und
    /// neuem Wert im Protokoll.
    /// </summary>
    /// <param name="context">Der Datenbankkontext.</param>
    /// <param name="replica">Der geladene Bestand.</param>
    /// <param name="clock">Die logische Uhr; sie sieht jeden fremden Zeitpunkt.</param>
    /// <param name="time">Zeitquelle für den Löschzeitpunkt.</param>
    /// <param name="logger">Protokollkanal.</param>
    internal sealed class ReplicaApplier(EchoPlayDbContext context, LocalReplica replica, IHybridClock clock, TimeProvider time, ILogger logger)
    {
        /// <summary>
        /// Übernimmt die Sätze in der Reihenfolge der Arten, Übergeordnetes zuerst; noch nicht
        /// gespeichert.
        /// </summary>
        /// <param name="records">Die fremden Sätze.</param>
        /// <returns>Wie viele Datensätze sich dadurch geändert haben.</returns>
        public int Apply(IEnumerable<ChangeRecord> records)
        {
            ArgumentNullException.ThrowIfNull(records);
            ILookup<string, ChangeRecord> byKind = records.ToLookup(r => r.Kind, StringComparer.Ordinal);
            int changed = 0;
            foreach (string kind in ChangeKinds.All)
            {
                foreach (IGrouping<string, ChangeRecord> sameKey in byKind[kind].GroupBy(r => r.Key, StringComparer.Ordinal))
                {
                    changed += ApplyOne(ChangeMerge.MergeAll(sameKey)) ? 1 : 0;
                }
            }

            return changed;
        }

        private bool ApplyOne(ChangeRecord incoming)
        {
            _ = clock.Observe(incoming.Stamp);
            bool exists = replica.TryGet(incoming.Kind, incoming.Key, out BaseEntity? entity, out ChangeRecord? local);
            ChangeRecord merged = exists ? ChangeMerge.Merge(local!, incoming) : incoming;
            if (exists && merged.Equals(local))
            {
                return false;
            }

            if (merged.Kind == ChangeKinds.DashboardOrder)
            {
                ApplyOrder(merged);
            }
            else
            {
                entity ??= merged.Deleted ? null : Create(merged);
                if (entity is null)
                {
                    return false;
                }

                Write(entity, merged, local);
            }

            replica.Remember(entity, merged);
            return true;
        }

        /// <summary>Legt die Entität zu einem neuen Satz an; fehlt das Übergeordnete, wird er übergangen.</summary>
        private BaseEntity? Create(ChangeRecord record)
        {
            BaseEntity? entity = record.Kind switch
            {
                ChangeKinds.Series => new Series(),
                ChangeKinds.Episode => replica.FindSeries(record.Parent) is { } series ? new Episode { SeriesId = series.Id, Series = series } : null,
                ChangeKinds.PlaybackState => replica.FindEpisode(record.Parent) is { } episode ? new PlaybackState { EpisodeId = episode.Id, Episode = episode } : null,
                ChangeKinds.WatchedTitle => new WatchedTitle(),
                ChangeKinds.DialogSuppression => new DialogSuppression(),
                ChangeKinds.Settings => new AppSettings(),
                _ => null,
            };

            if (entity is null)
            {
                logger.Warning("Abgleich: {Kind} {Key} übergangen, der übergeordnete Datensatz {Parent} fehlt hier.", record.Kind, record.Key, record.Parent ?? "-");
                return null;
            }

            _ = context.Add(entity);
            return entity;
        }

        /// <summary>Schreibt Felder, Löschung und Zeitpunkt des zusammengeführten Stands in die Entität.</summary>
        private void Write(BaseEntity entity, ChangeRecord merged, ChangeRecord? local)
        {
            List<string> rejected = SharedFields.Write(entity, merged.Fields);
            if (merged.Deleted && !entity.IsDeleted)
            {
                entity.MarkAsDeleted(time.GetUtcNow().UtcDateTime);
            }
            else if (!merged.Deleted && entity.IsDeleted)
            {
                entity.MarkAsRestored();
            }

            entity.MarkAsChanged(merged.Stamp);

            // Auch bei unverändertem Wert als gesetzt markieren: Sonst sähe der Stempel-Interceptor
            // geänderte Felder ohne gesetzten Zeitpunkt und stempelte die Übernahme als eigene Änderung.
            context.Entry(entity).Property(e => e.ChangeStamp).IsModified = true;
            Audit(merged, local, rejected);
        }

        /// <summary>
        /// Schreibt die Reihenfolge eines Startseiten-Abschnitts neu. Serien, die es hier nicht gibt,
        /// fallen heraus; eigene Serien, die die Liste nicht nennt, folgen in ihrer bisherigen Ordnung.
        /// </summary>
        private void ApplyOrder(ChangeRecord merged)
        {
            List<string> keys = [.. (merged.Field(ChangeKinds.OrderField) ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries)];
            List<DashboardPosition> rows = replica.Positions.TryGetValue(merged.Key, out List<DashboardPosition>? existing) ? existing : [];
            Dictionary<Guid, int> rank = [];
            foreach (string key in keys)
            {
                if (replica.FindSeries(key) is { } series)
                {
                    _ = rank.TryAdd(series.Id, rank.Count);
                }
            }

            int position = 0;
            foreach (DashboardPosition row in rows.OrderBy(r => rank.TryGetValue(r.SeriesId, out int r1) ? r1 : int.MaxValue).ThenBy(r => r.Position))
            {
                row.Position = position++;
                row.MarkAsChanged(merged.Stamp);
                context.Entry(row).Property(e => e.ChangeStamp).IsModified = true;
            }

            logger.Info("Abgleich: Reihenfolge {Section} übernommen (Gerät {Device}, {Stamp}).", merged.Key, merged.Stamp.DeviceId.ToString("N"), merged.Stamp.ToString());
        }

        /// <summary>Protokolliert eine Übernahme: Gerät, Zeitpunkt, je geändertem Feld alt und neu.</summary>
        private void Audit(ChangeRecord merged, ChangeRecord? local, List<string> rejected)
        {
            IEnumerable<string> changes = merged.Fields
                .Where(f => local is null || !string.Equals(local.Field(f.Key), f.Value, StringComparison.Ordinal))
                .Select(f => $"{f.Key}: {local?.Field(f.Key) ?? "-"} → {f.Value ?? "-"}");
            if (local is null || local.Deleted != merged.Deleted)
            {
                changes = changes.Append($"gelöscht: {(local?.Deleted ?? false ? "ja" : "nein")} → {(merged.Deleted ? "ja" : "nein")}");
            }

            logger.Info(
                "Abgleich: {Kind} {Key} übernommen (Gerät {Device}, {Stamp}): {Changes}",
                merged.Kind, merged.Key, merged.Stamp.DeviceId.ToString("N"), merged.Stamp.ToString(), string.Join("; ", changes));
            if (rejected.Count > 0)
            {
                logger.Warning("Abgleich: {Kind} {Key}, Felder nicht übernommen: {Fields}", merged.Kind, merged.Key, string.Join(", ", rejected));
            }
        }
    }
}
