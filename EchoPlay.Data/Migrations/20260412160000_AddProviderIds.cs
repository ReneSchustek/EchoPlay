using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bestehende Series-Spalten von MaxLength 128 auf 64 reduzieren.
            // SQLite ignoriert MaxLength-Änderungen zur Laufzeit, aber die Migration
            // dokumentiert die Absicht und hält den Snapshot konsistent.

            _ = migrationBuilder.AddColumn<string>(
                name: "SpotifyAlbumId",
                table: "Episodes",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            _ = migrationBuilder.AddColumn<string>(
                name: "AppleMusicAlbumId",
                table: "Episodes",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            // Index für Metadaten-Lookup: Spotify sucht Episoden über die AlbumId
            _ = migrationBuilder.CreateIndex(
                name: "IX_Episodes_SpotifyAlbumId",
                table: "Episodes",
                column: "SpotifyAlbumId",
                filter: "SpotifyAlbumId IS NOT NULL");

            // Index für Metadaten-Lookup: Apple Music sucht Episoden über die AlbumId
            _ = migrationBuilder.CreateIndex(
                name: "IX_Episodes_AppleMusicAlbumId",
                table: "Episodes",
                column: "AppleMusicAlbumId",
                filter: "AppleMusicAlbumId IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
        }
    }
}
