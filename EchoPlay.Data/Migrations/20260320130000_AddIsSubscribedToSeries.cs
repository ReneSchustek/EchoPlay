using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIsSubscribedToSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSubscribed",
                table: "Series",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Alle bestehenden Serien direkt abonnieren – Import und Abonnement sind dasselbe Konzept.
            // Serien, die vor dieser Migration existierten, wurden bewusst importiert und gelten
            // daher als abonniert. Soft-gelöschte Serien bleiben unverändert.
            migrationBuilder.Sql("UPDATE \"Series\" SET \"IsSubscribed\" = 1 WHERE \"IsDeleted\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSubscribed",
                table: "Series");
        }
    }
}
