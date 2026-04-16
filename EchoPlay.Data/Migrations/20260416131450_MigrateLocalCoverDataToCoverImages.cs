using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class MigrateLocalCoverDataToCoverImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Nachzügler-Migration: Seit 20260402140000 wurden zwar alle damaligen Cover in die
            // CoverImages-Tabelle kopiert, die Spalten Series.LocalCoverData / Episodes.LocalCoverData
            // blieben aber parallel bestehen und wurden weiter vom Code beschrieben. Hier werden
            // alle Einträge übernommen, für die bisher noch kein Eintrag in CoverImages existiert,
            // bevor die Spalten endgültig entfernt werden.
            _ = migrationBuilder.Sql("""
                INSERT INTO CoverImages (Id, EntityType, EntityId, ImageData, SourceUrl, LastChecked, CreatedAt, IsDeleted)
                SELECT
                    LOWER(HEX(RANDOMBLOB(4)) || '-' || HEX(RANDOMBLOB(2)) || '-4' || SUBSTR(HEX(RANDOMBLOB(2)),2) || '-' || SUBSTR('89ab', ABS(RANDOM()) % 4 + 1, 1) || SUBSTR(HEX(RANDOMBLOB(2)),2) || '-' || HEX(RANDOMBLOB(6))),
                    'Series',
                    s.Id,
                    s.LocalCoverData,
                    s.CoverImageUrl,
                    DATETIME('now'),
                    DATETIME('now'),
                    0
                FROM Series AS s
                WHERE s.LocalCoverData IS NOT NULL
                  AND LENGTH(s.LocalCoverData) > 0
                  AND s.IsDeleted = 0
                  AND NOT EXISTS (
                      SELECT 1 FROM CoverImages AS c
                      WHERE c.EntityType = 'Series' AND c.EntityId = s.Id
                  )
                """);

            _ = migrationBuilder.Sql("""
                INSERT INTO CoverImages (Id, EntityType, EntityId, ImageData, SourceUrl, LastChecked, CreatedAt, IsDeleted)
                SELECT
                    LOWER(HEX(RANDOMBLOB(4)) || '-' || HEX(RANDOMBLOB(2)) || '-4' || SUBSTR(HEX(RANDOMBLOB(2)),2) || '-' || SUBSTR('89ab', ABS(RANDOM()) % 4 + 1, 1) || SUBSTR(HEX(RANDOMBLOB(2)),2) || '-' || HEX(RANDOMBLOB(6))),
                    'Episode',
                    e.Id,
                    e.LocalCoverData,
                    e.CoverImageUrl,
                    e.CoverLastChecked,
                    DATETIME('now'),
                    0
                FROM Episodes AS e
                WHERE e.LocalCoverData IS NOT NULL
                  AND LENGTH(e.LocalCoverData) > 0
                  AND e.IsDeleted = 0
                  AND NOT EXISTS (
                      SELECT 1 FROM CoverImages AS c
                      WHERE c.EntityType = 'Episode' AND c.EntityId = e.Id
                  )
                """);

            _ = migrationBuilder.DropColumn(
                name: "LocalCoverData",
                table: "Series");

            _ = migrationBuilder.DropColumn(
                name: "LocalCoverData",
                table: "Episodes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.AddColumn<byte[]>(
                name: "LocalCoverData",
                table: "Series",
                type: "BLOB",
                maxLength: 5242880,
                nullable: true);

            _ = migrationBuilder.AddColumn<byte[]>(
                name: "LocalCoverData",
                table: "Episodes",
                type: "BLOB",
                maxLength: 5242880,
                nullable: true);
        }
    }
}
