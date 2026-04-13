using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLastAppStart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Zeitpunkt des letzten App-Starts – Referenz für den 60-Tage-Neuerscheinungen-Filter
            _ = migrationBuilder.AddColumn<DateTime>(
                name: "LastAppStart",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropColumn(
                name: "LastAppStart",
                table: "AppSettings");
        }
    }
}
