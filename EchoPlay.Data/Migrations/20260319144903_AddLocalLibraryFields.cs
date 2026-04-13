using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalLibraryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            _ = migrationBuilder.AddColumn<byte[]>(
                name: "LocalCoverData",
                table: "Series",
                type: "BLOB",
                nullable: true);

            _ = migrationBuilder.AddColumn<string>(
                name: "LocalFolderPath",
                table: "Series",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            _ = migrationBuilder.AddColumn<string>(
                name: "LocalFolderPath",
                table: "Episodes",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            _ = migrationBuilder.AddColumn<int>(
                name: "LocalTrackCount",
                table: "Episodes",
                type: "INTEGER",
                nullable: true);

            _ = migrationBuilder.AddColumn<int>(
                name: "TrackMatchKind",
                table: "Episodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            _ = migrationBuilder.CreateTable(
                name: "LocalTracks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    TrackNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    _ = table.PrimaryKey("PK_LocalTracks", x => x.Id);
                    _ = table.ForeignKey(
                        name: "FK_LocalTracks_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            _ = migrationBuilder.CreateIndex(
                name: "IX_LocalTracks_EpisodeId",
                table: "LocalTracks",
                column: "EpisodeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            _ = migrationBuilder.DropTable(
                name: "LocalTracks");

            _ = migrationBuilder.DropColumn(
                name: "LocalCoverData",
                table: "Series");

            _ = migrationBuilder.DropColumn(
                name: "LocalFolderPath",
                table: "Series");

            _ = migrationBuilder.DropColumn(
                name: "LocalFolderPath",
                table: "Episodes");

            _ = migrationBuilder.DropColumn(
                name: "LocalTrackCount",
                table: "Episodes");

            _ = migrationBuilder.DropColumn(
                name: "TrackMatchKind",
                table: "Episodes");
        }
    }
}
