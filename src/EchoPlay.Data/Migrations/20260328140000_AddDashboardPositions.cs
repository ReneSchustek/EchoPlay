using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardPositions : Migration
    {
        private static readonly string[] IndexColumns = ["SeriesId", "Section"];
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Neue Tabelle für benutzerdefinierte Dashboard-Sortierung
            _ = migrationBuilder.CreateTable(
                name: "DashboardPositions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeriesId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Section = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    _ = table.PrimaryKey("PK_DashboardPositions", x => x.Id);
                });

            _ = migrationBuilder.CreateIndex(
                name: "IX_DashboardPositions_SeriesId_Section",
                table: "DashboardPositions",
                columns: IndexColumns,
                unique: true);

            // Alte Spalte auf Series entfernen – Positionsdaten liegen jetzt in DashboardPositions
            _ = migrationBuilder.DropColumn(
                name: "DashboardSortOrder",
                table: "Series");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropTable(name: "DashboardPositions");

            _ = migrationBuilder.AddColumn<int>(
                name: "DashboardSortOrder",
                table: "Series",
                type: "INTEGER",
                nullable: true);
        }
    }
}
