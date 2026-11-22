using System.Data.Common;
using EchoPlay.Data.Configurations;
using EchoPlay.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace EchoPlay.Data.Tests.Infrastructure
{
    /// <summary>
    /// Wacht darüber, dass die Teilindizes mit Löschfilter für EF-Abfragen zulässig bleiben.
    /// </summary>
    /// <remarks>
    /// SQLite nimmt einen Teilindex nur, wenn es den Filter des Index formal aus der Abfrage
    /// ableiten kann. Ein Index mit <c>IsDeleted = 0</c> ist für eine Abfrage mit
    /// <c>NOT ("x"."IsDeleted")</c> unbrauchbar, ohne dass irgendetwas fehlschlägt; die Abfrage wird
    /// nur langsamer. Deshalb prüft der erste Test die Form jedes Filters im Modell und die beiden
    /// übrigen am Plan, dass SQLite die Indizes tatsächlich benutzt.
    /// </remarks>
    public sealed class SoftDeleteIndexFilterTests : IDisposable
    {
        private readonly EchoPlayDbContext _context = SqliteInMemoryDbContextFactory.Create();

        [Fact]
        public void EveryIndexWithDeleteFilter_UsesTheFormEfCoreWrites()
        {
            List<string> wrong = [.. _context.Model.GetEntityTypes()
                .SelectMany(e => e.GetIndexes())
                .Where(i => i.GetFilter()?.Contains("IsDeleted", StringComparison.Ordinal) == true)
                .Where(i => !HasExpectedForm(i.GetFilter()!))
                .Select(i => $"{i.GetDatabaseName()}: {i.GetFilter()}")];

            Assert.Empty(wrong);
        }

        [Fact]
        public void CoverBatchQuery_UsesEntityIndex()
        {
            // Der Text entspricht dem, was EF Core für GetImageDataByEntitiesAsync erzeugt; der Bericht
            // der Abfragepläne im Messprojekt prüft den echten, mitgeschnittenen Befehl.
            using DbCommand command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                EXPLAIN QUERY PLAN
                SELECT "c"."Id", "c"."ImageData"
                FROM "CoverImages" AS "c"
                WHERE NOT ("c"."IsDeleted") AND "c"."EntityType" = 'Episode' AND "c"."EntityId" IN ('a', 'b', 'c')
                """;

            string plan = ReadPlan(command);

            Assert.Contains("IX_CoverImages_EntityType_EntityId", plan, StringComparison.Ordinal);
        }

        [Fact]
        public void PurgeQuery_UsesPurgeIndex()
        {
            // Wie die Bereinigung in DatabaseMaintenanceService löscht: ohne Löschfilter, nur gelöschte Zeilen.
            using DbCommand command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                EXPLAIN QUERY PLAN
                DELETE FROM "Episodes" AS "e"
                WHERE "e"."IsDeleted" AND "e"."DeletedAt" < '2026-01-01'
                """;

            string plan = ReadPlan(command);

            Assert.Contains("IX_Episodes_IsDeleted_DeletedAt", plan, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public void Dispose() => _context.Dispose();

        private static bool HasExpectedForm(string filter) =>
            filter == SoftDeleteIndexFilter.ActiveRows
            || filter.StartsWith(SoftDeleteIndexFilter.ActiveRows + " AND ", StringComparison.Ordinal)
            || filter == SoftDeleteIndexFilter.DeletedRows;

        /// <summary>Liest die Spalte <c>detail</c> jeder Planzeile.</summary>
        private static string ReadPlan(DbCommand command)
        {
            List<string> details = [];
            using DbDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                details.Add(reader.GetString(3));
            }

            return string.Join(Environment.NewLine, details);
        }
    }
}
