using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceHashSecureSettingsProviderIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.AddColumn<string>(
                name: "AppleMusicAlbumId",
                table: "Episodes",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            _ = migrationBuilder.AddColumn<string>(
                name: "SpotifyAlbumId",
                table: "Episodes",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            _ = migrationBuilder.AddColumn<string>(
                name: "SourceHash",
                table: "CoverImages",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            _ = migrationBuilder.CreateTable(
                name: "SecureSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EncryptedValue = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    _ = table.PrimaryKey("PK_SecureSettings", x => x.Id);
                });

            _ = migrationBuilder.CreateIndex(
                name: "IX_Episodes_AppleMusicAlbumId",
                table: "Episodes",
                column: "AppleMusicAlbumId",
                filter: "AppleMusicAlbumId IS NOT NULL");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Episodes_SpotifyAlbumId",
                table: "Episodes",
                column: "SpotifyAlbumId",
                filter: "SpotifyAlbumId IS NOT NULL");

            _ = migrationBuilder.CreateIndex(
                name: "IX_SecureSettings_Key",
                table: "SecureSettings",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropTable(
                name: "SecureSettings");

            _ = migrationBuilder.DropIndex(
                name: "IX_Episodes_AppleMusicAlbumId",
                table: "Episodes");

            _ = migrationBuilder.DropIndex(
                name: "IX_Episodes_SpotifyAlbumId",
                table: "Episodes");

            _ = migrationBuilder.DropColumn(
                name: "AppleMusicAlbumId",
                table: "Episodes");

            _ = migrationBuilder.DropColumn(
                name: "SpotifyAlbumId",
                table: "Episodes");

            _ = migrationBuilder.DropColumn(
                name: "SourceHash",
                table: "CoverImages");
        }
    }
}
