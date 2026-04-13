using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesIsWatched : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Überwachungs-Flag: nur überwachte Serien erscheinen
            // unter Neuerscheinungen/Ankündigungen. Default: true.
            _ = migrationBuilder.AddColumn<bool>(
                name: "IsWatched",
                table: "Series",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            _ = migrationBuilder.DropColumn(
                name: "IsWatched",
                table: "Series");
        }
    }
}
