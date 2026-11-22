using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Neue Spalte ChangeStamp (TEXT, NULL erlaubt) in jeder Tabelle: der Zeitpunkt der hybriden
            // logischen Uhr, zu dem sich ein gemeinsames Feld der Zeile zuletzt geändert hat. NULL für den
            // Bestand heißt: seit Einführung der Uhr nicht geändert. Ein Vorgabewert wäre falsch, denn er
            // gäbe allen alten Zeilen denselben Zeitpunkt. Reine Erweiterung, keine Daten werden angefasst.

            _ = migrationBuilder.AddColumn<string>(
                name: "ChangeStamp",
                table: "WatchedTitles",
                type: "TEXT",
                nullable: true);

            _ = migrationBuilder.AddColumn<string>(
                name: "ChangeStamp",
                table: "Series",
                type: "TEXT",
                nullable: true);

            _ = migrationBuilder.AddColumn<string>(
                name: "ChangeStamp",
                table: "SecureSettings",
                type: "TEXT",
                nullable: true);

            _ = migrationBuilder.AddColumn<string>(
                name: "ChangeStamp",
                table: "PlaybackStates",
                type: "TEXT",
                nullable: true);

            _ = migrationBuilder.AddColumn<string>(
                name: "ChangeStamp",
                table: "LocalTracks",
                type: "TEXT",
                nullable: true);

            _ = migrationBuilder.AddColumn<string>(
                name: "ChangeStamp",
                table: "Episodes",
                type: "TEXT",
                nullable: true);

            _ = migrationBuilder.AddColumn<string>(
                name: "ChangeStamp",
                table: "DialogSuppressions",
                type: "TEXT",
                nullable: true);

            _ = migrationBuilder.AddColumn<string>(
                name: "ChangeStamp",
                table: "DashboardPositions",
                type: "TEXT",
                nullable: true);

            _ = migrationBuilder.AddColumn<string>(
                name: "ChangeStamp",
                table: "CoverImages",
                type: "TEXT",
                nullable: true);

            _ = migrationBuilder.AddColumn<string>(
                name: "ChangeStamp",
                table: "CachedNewReleases",
                type: "TEXT",
                nullable: true);

            _ = migrationBuilder.AddColumn<string>(
                name: "ChangeStamp",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropColumn(
                name: "ChangeStamp",
                table: "WatchedTitles");

            _ = migrationBuilder.DropColumn(
                name: "ChangeStamp",
                table: "Series");

            _ = migrationBuilder.DropColumn(
                name: "ChangeStamp",
                table: "SecureSettings");

            _ = migrationBuilder.DropColumn(
                name: "ChangeStamp",
                table: "PlaybackStates");

            _ = migrationBuilder.DropColumn(
                name: "ChangeStamp",
                table: "LocalTracks");

            _ = migrationBuilder.DropColumn(
                name: "ChangeStamp",
                table: "Episodes");

            _ = migrationBuilder.DropColumn(
                name: "ChangeStamp",
                table: "DialogSuppressions");

            _ = migrationBuilder.DropColumn(
                name: "ChangeStamp",
                table: "DashboardPositions");

            _ = migrationBuilder.DropColumn(
                name: "ChangeStamp",
                table: "CoverImages");

            _ = migrationBuilder.DropColumn(
                name: "ChangeStamp",
                table: "CachedNewReleases");

            _ = migrationBuilder.DropColumn(
                name: "ChangeStamp",
                table: "AppSettings");
        }
    }
}
