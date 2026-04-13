using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNewReleaseDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Konfigurierbares Zeitfenster für Neuerscheinungen (Standard: 90 Tage)
            _ = migrationBuilder.AddColumn<int>(
                name: "NewReleaseDays",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 90);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            _ = migrationBuilder.DropColumn(
                name: "NewReleaseDays",
                table: "AppSettings");
        }
    }
}
