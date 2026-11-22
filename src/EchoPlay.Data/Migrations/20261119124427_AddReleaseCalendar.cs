using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReleaseCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Ende des letzten vollständigen Kalenderaufbaus; leer, bis der erste fertig ist.
            _ = migrationBuilder.AddColumn<DateTime>(
                name: "ReleaseCalendarRefreshedAtUtc",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);

            // Eigene Tabelle neben CachedNewReleases: Der Kalender gilt für alle Serien und
            // reicht bis zum Vormonat zurück, die Neuerscheinungen nur für überwachte Serien
            // im eingestellten Zeitfenster. Beide Aufräumregeln vertragen sich nicht in einer Tabelle.
            _ = migrationBuilder.CreateTable(
                name: "ReleaseCalendarEntries",
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
                    _ = table.PrimaryKey("PK_ReleaseCalendarEntries", x => x.Id);
                    _ = table.ForeignKey(
                        name: "FK_ReleaseCalendarEntries_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            _ = migrationBuilder.CreateIndex(
                name: "IX_ReleaseCalendarEntries_CollectionId",
                table: "ReleaseCalendarEntries",
                column: "CollectionId",
                unique: true);

            _ = migrationBuilder.CreateIndex(
                name: "IX_ReleaseCalendarEntries_ReleaseDate",
                table: "ReleaseCalendarEntries",
                column: "ReleaseDate");

            _ = migrationBuilder.CreateIndex(
                name: "IX_ReleaseCalendarEntries_SeriesId",
                table: "ReleaseCalendarEntries",
                column: "SeriesId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropTable(
                name: "ReleaseCalendarEntries");

            _ = migrationBuilder.DropColumn(
                name: "ReleaseCalendarRefreshedAtUtc",
                table: "AppSettings");
        }
    }
}
