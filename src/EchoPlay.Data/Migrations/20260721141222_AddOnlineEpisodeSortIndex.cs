using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlineEpisodeSortIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Standard 0 = Nummer aufsteigend; Bestandsinstallationen starten damit auf der
            // bisherigen Standard-Sortierung, bis der Nutzer eine andere wählt.
            _ = migrationBuilder.AddColumn<int>(
                name: "OnlineEpisodeSortIndex",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropColumn(
                name: "OnlineEpisodeSortIndex",
                table: "AppSettings");
        }
    }
}
