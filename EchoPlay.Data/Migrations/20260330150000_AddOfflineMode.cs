using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOfflineMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Offline-Modus: deaktiviert Online-Abfragen (iTunes, Suche).
            // Standard: false (Online-Funktionen aktiv).
            _ = migrationBuilder.AddColumn<bool>(
                name: "OfflineMode",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            _ = migrationBuilder.DropColumn(
                name: "OfflineMode",
                table: "AppSettings");
        }
    }
}
