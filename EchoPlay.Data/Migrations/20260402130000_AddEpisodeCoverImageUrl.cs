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
            // Provider-Cover-URL – bleibt auch nach dem Import verfügbar,
            // damit Cover jederzeit nachgeladen werden können.
            migrationBuilder.AddColumn<string>(
                name: "CoverImageUrl",
                table: "Episodes",
                type: "TEXT",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoverImageUrl",
                table: "Episodes");
        }
    }
}
