using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSortIndexesAndSoftDeleteFilters : Migration
    {
        // Vermeidet CA1861 (wiederholte konstante Array-Argumente).
        private static readonly string[] CoverImageEntityColumns = ["EntityType", "EntityId"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropIndex(
                name: "IX_SecureSettings_Key",
                table: "SecureSettings");

            _ = migrationBuilder.DropIndex(
                name: "IX_CoverImages_EntityType_EntityId",
                table: "CoverImages");

            _ = migrationBuilder.CreateIndex(
                name: "IX_SecureSettings_Key",
                table: "SecureSettings",
                column: "Key",
                unique: true,
                filter: "IsDeleted = 0");

            _ = migrationBuilder.CreateIndex(
                name: "IX_PlaybackStates_LastPlayedAt",
                table: "PlaybackStates",
                column: "LastPlayedAt",
                filter: "IsDeleted = 0 AND LastPlayedAt IS NOT NULL");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Episodes_ReleaseDate",
                table: "Episodes",
                column: "ReleaseDate",
                filter: "IsDeleted = 0 AND ReleaseDate IS NOT NULL");

            _ = migrationBuilder.CreateIndex(
                name: "IX_CoverImages_EntityType_EntityId",
                table: "CoverImages",
                columns: CoverImageEntityColumns,
                unique: true,
                filter: "IsDeleted = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropIndex(
                name: "IX_SecureSettings_Key",
                table: "SecureSettings");

            _ = migrationBuilder.DropIndex(
                name: "IX_PlaybackStates_LastPlayedAt",
                table: "PlaybackStates");

            _ = migrationBuilder.DropIndex(
                name: "IX_Episodes_ReleaseDate",
                table: "Episodes");

            _ = migrationBuilder.DropIndex(
                name: "IX_CoverImages_EntityType_EntityId",
                table: "CoverImages");

            _ = migrationBuilder.CreateIndex(
                name: "IX_SecureSettings_Key",
                table: "SecureSettings",
                column: "Key",
                unique: true);

            _ = migrationBuilder.CreateIndex(
                name: "IX_CoverImages_EntityType_EntityId",
                table: "CoverImages",
                columns: CoverImageEntityColumns,
                unique: true);
        }
    }
}
