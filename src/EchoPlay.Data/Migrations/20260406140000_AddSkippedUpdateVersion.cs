using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSkippedUpdateVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.AddColumn<string>(
                name: "SkippedUpdateVersion",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropColumn(
                name: "SkippedUpdateVersion",
                table: "AppSettings");
        }
    }
}
