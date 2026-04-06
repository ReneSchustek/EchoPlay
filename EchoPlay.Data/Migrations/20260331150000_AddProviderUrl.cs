using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // URL zum Öffnen der Folge beim Provider (Spotify/Apple Music).
            // Wird beim Online-Import gesetzt. Null bei lokalen Folgen.
            migrationBuilder.AddColumn<string>(
                name: "ProviderUrl",
                table: "Episodes",
                type: "TEXT",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProviderUrl",
                table: "Episodes");
        }
    }
}
