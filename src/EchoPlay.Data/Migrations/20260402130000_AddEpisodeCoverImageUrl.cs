using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEpisodeCoverImageUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Provider-Cover-URL – bleibt auch nach dem Import verfügbar,
            // damit Cover jederzeit nachgeladen werden können.
            _ = migrationBuilder.AddColumn<string>(
                name: "CoverImageUrl",
                table: "Episodes",
                type: "TEXT",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropColumn(
                name: "CoverImageUrl",
                table: "Episodes");
        }
    }
}
