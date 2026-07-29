using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesCoverLastChecked : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Cooldown-Zeitstempel für die automatische Serien-Cover-Suche, Gegenstück zu
            // Episodes.CoverLastChecked. Ohne die Spalte fragt der Hintergrunddienst bei jedem
            // Durchlauf dieselben coverlosen Serien erneut bei den Anbietern an.
            // NULL für den Bestand ist genau richtig: „noch nie geprüft" – die erste Suche
            // läuft also für alle bestehenden Serien.
            _ = migrationBuilder.AddColumn<DateTime>(
                name: "CoverLastChecked",
                table: "Series",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropColumn(
                name: "CoverLastChecked",
                table: "Series");
        }
    }
}
