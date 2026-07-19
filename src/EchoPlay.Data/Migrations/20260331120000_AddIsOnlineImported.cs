using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIsOnlineImported : Migration
    {
        private static readonly string[] IndexColumns = ["IsOnlineImported", "Title"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Neues Flag: trennt Online-importierte Serien von lokal gescannten.
            // Standard: false – bestehende lokal gescannte Serien bleiben unsichtbar in der Online-Mediathek.
            _ = migrationBuilder.AddColumn<bool>(
                name: "IsOnlineImported",
                table: "Series",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Datenmigration: Serien die ausschließlich über Online-Import existieren
            // (Provider-ID vorhanden, aber kein lokaler Ordner) → definitiv Online-Import.
            // Serien mit Provider-ID UND lokalem Ordner → im Zweifel als Online-Import markieren,
            // damit sie in der Online-Mediathek sichtbar bleiben.
            _ = migrationBuilder.Sql("""
                UPDATE Series
                SET IsOnlineImported = 1
                WHERE (SpotifyArtistId IS NOT NULL OR AppleMusicArtistId IS NOT NULL)
                  AND IsDeleted = 0
                """);

            // Index für die Online-Mediathek: schneller Filter auf IsOnlineImported.
            _ = migrationBuilder.CreateIndex(
                name: "IX_Series_IsOnlineImported_Title",
                table: "Series",
                columns: IndexColumns,
                filter: "IsDeleted = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropIndex(
                name: "IX_Series_IsOnlineImported_Title",
                table: "Series");

            _ = migrationBuilder.DropColumn(
                name: "IsOnlineImported",
                table: "Series");
        }
    }
}
