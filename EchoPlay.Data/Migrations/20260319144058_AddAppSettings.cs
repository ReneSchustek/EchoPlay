using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAppSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            _ = migrationBuilder.DropForeignKey(
                name: "FK_Episodes_Series_SeriesId",
                table: "Episodes");

            _ = migrationBuilder.DropForeignKey(
                name: "FK_PlaybackStates_Episodes_EpisodeId",
                table: "PlaybackStates");

            _ = migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActiveProvider = table.Column<int>(type: "INTEGER", nullable: false),
                    LocalLibraryEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LocalLibraryRootPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    EpisodeFolderPattern = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SaveCoverToDirectory = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    _ = table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            _ = migrationBuilder.AddForeignKey(
                name: "FK_Episodes_Series_SeriesId",
                table: "Episodes",
                column: "SeriesId",
                principalTable: "Series",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            _ = migrationBuilder.AddForeignKey(
                name: "FK_PlaybackStates_Episodes_EpisodeId",
                table: "PlaybackStates",
                column: "EpisodeId",
                principalTable: "Episodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            _ = migrationBuilder.DropForeignKey(
                name: "FK_Episodes_Series_SeriesId",
                table: "Episodes");

            _ = migrationBuilder.DropForeignKey(
                name: "FK_PlaybackStates_Episodes_EpisodeId",
                table: "PlaybackStates");

            _ = migrationBuilder.DropTable(
                name: "AppSettings");

            _ = migrationBuilder.AddForeignKey(
                name: "FK_Episodes_Series_SeriesId",
                table: "Episodes",
                column: "SeriesId",
                principalTable: "Series",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            _ = migrationBuilder.AddForeignKey(
                name: "FK_PlaybackStates_Episodes_EpisodeId",
                table: "PlaybackStates",
                column: "EpisodeId",
                principalTable: "Episodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
