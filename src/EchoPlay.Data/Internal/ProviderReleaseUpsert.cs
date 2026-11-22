using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Contracts;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Internal
{
    /// <summary>Was ein Upsert von Anbieter-Alben ergeben hat.</summary>
    /// <param name="Inserted">Neu angelegte Zeilen.</param>
    /// <param name="Updated">Aktualisierte Zeilen.</param>
    /// <param name="Conflict">Ein UNIQUE-Konflikt mit einem zweiten Kontext, sonst leer.</param>
    internal sealed record ProviderReleaseUpsertResult(int Inserted, int Updated, DbUpdateException? Conflict);

    /// <summary>
    /// Schreibt Alben des Anbieters in eine Tabelle mit eindeutiger Sammlungs-Kennung: neue anlegen,
    /// vorhandene aktualisieren, logisch gelöschte durch die neue Antwort ersetzen.
    /// </summary>
    internal static class ProviderReleaseUpsert
    {
        /// <summary>
        /// Schreibt die Einträge. Dieselbe Kennung kann mehrfach hereinkommen, etwa über zwei Serien
        /// desselben Künstlers; der letzte gewinnt.
        /// </summary>
        /// <typeparam name="T">Die Entität.</typeparam>
        /// <param name="context">Der Datenbankkontext.</param>
        /// <param name="set">Die Tabelle.</param>
        /// <param name="entries">Die neuen Antworten des Anbieters.</param>
        /// <param name="now">Zeitpunkt der Änderung für aktualisierte Zeilen.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Zahlen und ein etwaiger Konflikt.</returns>
        public static async Task<ProviderReleaseUpsertResult> UpsertAsync<T>(
            EchoPlayDbContext context, DbSet<T> set, IReadOnlyList<T> entries, DateTime now, CancellationToken cancellationToken)
            where T : BaseEntity, IProviderRelease
        {
            Dictionary<long, T> unique = [];
            foreach (T entry in entries)
            {
                unique[entry.CollectionId] = entry;
            }

            // Ohne Abfragefilter, weil der eindeutige Index auch logisch gelöschte Zeilen zählt.
            List<long> collectionIds = [.. unique.Keys];
            Dictionary<long, T> existing = await set
                .AsTracking()
                .IgnoreQueryFilters()
                .Where(e => collectionIds.Contains(e.CollectionId))
                .ToDictionaryAsync(e => e.CollectionId, cancellationToken)
                .ConfigureAwait(false);

            int inserted = 0;
            foreach (T entry in unique.Values)
            {
                inserted += Apply(set, existing.GetValueOrDefault(entry.CollectionId), entry, now) ? 1 : 0;
            }

            DbUpdateException? conflict = await context.TrySaveChangesIgnoreUniqueAsync(cancellationToken).ConfigureAwait(false);
            return new ProviderReleaseUpsertResult(inserted, unique.Count - inserted, conflict);
        }

        /// <summary>
        /// Legt an oder aktualisiert. Eine logisch gelöschte Zeile darf weg: Die Einträge sind
        /// Antworten des Anbieters, keine Nutzerdaten.
        /// </summary>
        /// <returns><see langword="true"/>, wenn eine Zeile neu angelegt wurde.</returns>
        private static bool Apply<T>(DbSet<T> set, T? existing, T entry, DateTime now)
            where T : BaseEntity, IProviderRelease
        {
            if (existing is null || existing.IsDeleted)
            {
                if (existing is not null)
                {
                    _ = set.Remove(existing);
                }

                _ = set.Add(entry);
                return true;
            }

            existing.Title = entry.Title;
            existing.SeriesId = entry.SeriesId;
            existing.EpisodeNumber = entry.EpisodeNumber;
            existing.ReleaseDate = entry.ReleaseDate;
            existing.CoverAddress = entry.CoverAddress;
            existing.CheckedAtUtc = entry.CheckedAtUtc;
            existing.MarkAsUpdated(now);
            return false;
        }
    }
}
