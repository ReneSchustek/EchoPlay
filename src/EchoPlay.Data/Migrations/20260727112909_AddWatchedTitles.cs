using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchedTitles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Merkliste der überwachten Serientitel. Bewusst eine eigene Tabelle statt einer
            // Spalte an Series: beim Leeren der Mediathek verschwinden die Series-Zeilen
            // physisch, die Überwachung soll das überleben.
            // Der Erstbestand wird nicht hier befüllt, sondern beim Start über den Normalizer
            // abgeglichen – SQL kann dessen Regeln nicht identisch nachbilden.
            _ = migrationBuilder.CreateTable(
                name: "WatchedTitles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    NormalizedTitle = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    _ = table.PrimaryKey("PK_WatchedTitles", x => x.Id);
                });

            _ = migrationBuilder.CreateIndex(
                name: "IX_WatchedTitles_NormalizedTitle",
                table: "WatchedTitles",
                column: "NormalizedTitle",
                unique: true,
                filter: "IsDeleted = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropTable(
                name: "WatchedTitles");
        }
    }
}
