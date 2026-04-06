using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCachedNewReleases : Migration
    {
        private static readonly string[] IndexColumns = ["SeriesId", "ReleaseDate"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tabelle für gecachte iTunes-Neuerscheinungen.
            // Ermöglicht sofortige Dashboard-Anzeige ohne API-Wartezeit.
            migrationBuilder.CreateTable(
                name: "CachedNewReleases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeriesId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    ReleaseDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CoverUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CollectionId = table.Column<long>(type: "INTEGER", nullable: false),
                    CheckedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CachedNewReleases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CachedNewReleases_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Unique Index auf CollectionId: ein iTunes-Album darf nur einmal im Cache liegen
            migrationBuilder.CreateIndex(
                name: "IX_CachedNewReleases_CollectionId",
                table: "CachedNewReleases",
                column: "CollectionId",
                unique: true);

            // Kombi-Index für schnelle Abfragen nach Serie + Zeitfenster
            migrationBuilder.CreateIndex(
                name: "IX_CachedNewReleases_SeriesId_ReleaseDate",
                table: "CachedNewReleases",
                columns: IndexColumns);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CachedNewReleases");
        }
    }
}
