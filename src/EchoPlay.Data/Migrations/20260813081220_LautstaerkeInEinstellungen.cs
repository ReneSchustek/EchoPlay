using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class LautstaerkeInEinstellungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.AddColumn<bool>(
                name: "IsMuted",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Von Hand auf 1,0 gesetzt: Der Initialisierer der Entität gilt nur für neue
            // Objekte, nicht für die vorhandene Zeile. Mit dem erzeugten Vorgabewert 0,0
            // wäre die Anwendung nach dem Update stumm gewesen — und niemand hätte den
            // Regler gesucht, den es vorher nicht gab.
            _ = migrationBuilder.AddColumn<double>(
                name: "Volume",
                table: "AppSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 1.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropColumn(
                name: "IsMuted",
                table: "AppSettings");

            _ = migrationBuilder.DropColumn(
                name: "Volume",
                table: "AppSettings");
        }
    }
}
