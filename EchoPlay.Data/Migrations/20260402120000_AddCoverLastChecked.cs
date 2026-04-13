using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCoverLastChecked : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Zeitpunkt der letzten Cover-Suche – verhindert wiederholtes
            // Durchsuchen bei Episoden ohne Treffer (Cooldown: 7 Tage).
            _ = migrationBuilder.AddColumn<DateTime>(
                name: "CoverLastChecked",
                table: "Episodes",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropColumn(
                name: "CoverLastChecked",
                table: "Episodes");
        }
    }
}
