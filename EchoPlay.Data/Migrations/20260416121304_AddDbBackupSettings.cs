using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDbBackupSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Für Bestandsinstallationen weiterhin Backups aktivieren – erst ein expliziter
            // Opt-Out in den Einstellungen schaltet die Funktion ab.
            _ = migrationBuilder.AddColumn<bool>(
                name: "DbBackupEnabled",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            _ = migrationBuilder.AddColumn<int>(
                name: "DbBackupRetentionCount",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropColumn(
                name: "DbBackupEnabled",
                table: "AppSettings");

            _ = migrationBuilder.DropColumn(
                name: "DbBackupRetentionCount",
                table: "AppSettings");
        }
    }
}
