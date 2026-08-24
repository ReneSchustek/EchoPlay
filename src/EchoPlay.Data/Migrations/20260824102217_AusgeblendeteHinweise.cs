using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AusgeblendeteHinweise : Migration
    {
        // Vermeidet CA1861 (wiederholte konstante Array-Argumente).
        private static readonly string[] PurgeIndexColumns = ["IsDeleted", "DeletedAt"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Eine Zeile je ausgeblendetem Dialog. Bewusst eine eigene Tabelle statt weiterer
            // Spalten an AppSettings: Die Anwendung kennt rund fünfzig unterdrückbare Dialoge,
            // jeder weitere bekäme sonst eine eigene Spalte samt Migration.
            _ = migrationBuilder.CreateTable(
                name: "DialogSuppressions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    _ = table.PrimaryKey("PK_DialogSuppressions", x => x.Id);
                });

            _ = migrationBuilder.CreateIndex(
                name: "IX_DialogSuppressions_IsDeleted_DeletedAt",
                table: "DialogSuppressions",
                columns: PurgeIndexColumns,
                filter: "IsDeleted = 1");

            // Gefiltert auf aktive Zeilen: Ein zurückgeholter Hinweis lässt sich danach
            // erneut ausblenden, ohne am UNIQUE-Index anzuecken.
            _ = migrationBuilder.CreateIndex(
                name: "IX_DialogSuppressions_Key",
                table: "DialogSuppressions",
                column: "Key",
                unique: true,
                filter: "IsDeleted = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropTable(
                name: "DialogSuppressions");
        }
    }
}
