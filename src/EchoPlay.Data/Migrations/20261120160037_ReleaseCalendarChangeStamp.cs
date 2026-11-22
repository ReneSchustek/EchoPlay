using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseCalendarChangeStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Der Veröffentlichungskalender entstand neben der Einführung von ChangeStamp und
            // bekommt die Spalte hier nach, wie alle übrigen Tabellen. NULL heißt: seit Einführung
            // der Uhr nicht geändert. Reine Erweiterung, keine Daten werden angefasst.
            _ = migrationBuilder.AddColumn<string>(
                name: "ChangeStamp",
                table: "ReleaseCalendarEntries",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropColumn(
                name: "ChangeStamp",
                table: "ReleaseCalendarEntries");
        }
    }
}
