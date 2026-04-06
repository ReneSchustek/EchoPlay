using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCoverImagesTable : Migration
    {
        private static readonly string[] _entityTypeEntityId = ["EntityType", "EntityId"];
        private static readonly string[] _entityTypeLastChecked = ["EntityType", "LastChecked"];
        private static readonly string[] _isDeletedDeletedAt = ["IsDeleted", "DeletedAt"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Neue Tabelle für Cover-Binärdaten – getrennt von Metadaten
            migrationBuilder.CreateTable(
                name: "CoverImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImageData = table.Column<byte[]>(type: "BLOB", maxLength: 5242880, nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    LastChecked = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoverImages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoverImages_EntityType_EntityId",
                table: "CoverImages",
                columns: _entityTypeEntityId,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CoverImages_EntityType_LastChecked",
                table: "CoverImages",
                columns: _entityTypeLastChecked);

            migrationBuilder.CreateIndex(
                name: "IX_CoverImages_IsDeleted_DeletedAt",
                table: "CoverImages",
                columns: _isDeletedDeletedAt,
                filter: "IsDeleted = 1");

            // Bestehende Serien-Cover in die neue Tabelle übertragen
            migrationBuilder.Sql("""
                INSERT INTO CoverImages (Id, EntityType, EntityId, ImageData, SourceUrl, LastChecked, CreatedAt, IsDeleted)
                SELECT
                    LOWER(HEX(RANDOMBLOB(4)) || '-' || HEX(RANDOMBLOB(2)) || '-4' || SUBSTR(HEX(RANDOMBLOB(2)),2) || '-' || SUBSTR('89ab', ABS(RANDOM()) % 4 + 1, 1) || SUBSTR(HEX(RANDOMBLOB(2)),2) || '-' || HEX(RANDOMBLOB(6))),
                    'Series',
                    Id,
                    LocalCoverData,
                    CoverImageUrl,
                    DATETIME('now'),
                    DATETIME('now'),
                    0
                FROM Series
                WHERE LocalCoverData IS NOT NULL AND LENGTH(LocalCoverData) > 0 AND IsDeleted = 0
                """);

            // Bestehende Episoden-Cover in die neue Tabelle übertragen
            migrationBuilder.Sql("""
                INSERT INTO CoverImages (Id, EntityType, EntityId, ImageData, SourceUrl, LastChecked, CreatedAt, IsDeleted)
                SELECT
                    LOWER(HEX(RANDOMBLOB(4)) || '-' || HEX(RANDOMBLOB(2)) || '-4' || SUBSTR(HEX(RANDOMBLOB(2)),2) || '-' || SUBSTR('89ab', ABS(RANDOM()) % 4 + 1, 1) || SUBSTR(HEX(RANDOMBLOB(2)),2) || '-' || HEX(RANDOMBLOB(6))),
                    'Episode',
                    Id,
                    LocalCoverData,
                    CoverImageUrl,
                    CoverLastChecked,
                    DATETIME('now'),
                    0
                FROM Episodes
                WHERE LocalCoverData IS NOT NULL AND LENGTH(LocalCoverData) > 0 AND IsDeleted = 0
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CoverImages");
        }
    }
}
