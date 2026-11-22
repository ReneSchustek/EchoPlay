using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class PartialIndexesMatchSoftDeleteFilter : Migration
    {
        // Vermeidet CA1861 (wiederholte konstante Array-Argumente).
        private static readonly string[] EntityTypeEntityIdColumns = ["EntityType", "EntityId"];
        private static readonly string[] IsCompletedEpisodeIdColumns = ["IsCompleted", "EpisodeId"];
        private static readonly string[] IsDeletedDeletedAtColumns = ["IsDeleted", "DeletedAt"];
        private static readonly string[] IsFavoriteTitleColumns = ["IsFavorite", "Title"];
        private static readonly string[] IsOnlineImportedTitleColumns = ["IsOnlineImported", "Title"];
        private static readonly string[] IsSubscribedTitleColumns = ["IsSubscribed", "Title"];
        private static readonly string[] SectionPositionColumns = ["Section", "Position"];
        private static readonly string[] SeriesIdLocalFolderPathColumns = ["SeriesId", "LocalFolderPath"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Nur die Filter der Teilindizes ändern sich, von IsDeleted = 0 auf NOT "IsDeleted"
            // und von IsDeleted = 1 auf "IsDeleted". SQLite benutzt einen Teilindex nur, wenn es
            // den Filter aus der Abfrage ableiten kann, und das gelingt nur mit der Form, die der
            // Löschfilter von EF Core schreibt. Spalten, Eindeutigkeit und Namen bleiben gleich;
            // die Indizes werden neu aufgebaut, die Daten nicht angefasst.
            _ = migrationBuilder.DropIndex(
                name: "IX_WatchedTitles_NormalizedTitle",
                table: "WatchedTitles");

            _ = migrationBuilder.DropIndex(
                name: "IX_Series_IsDeleted_DeletedAt",
                table: "Series");

            _ = migrationBuilder.DropIndex(
                name: "IX_Series_IsFavorite_Title",
                table: "Series");

            _ = migrationBuilder.DropIndex(
                name: "IX_Series_IsOnlineImported_Title",
                table: "Series");

            _ = migrationBuilder.DropIndex(
                name: "IX_Series_IsSubscribed_Title",
                table: "Series");

            _ = migrationBuilder.DropIndex(
                name: "IX_SecureSettings_Key",
                table: "SecureSettings");

            _ = migrationBuilder.DropIndex(
                name: "IX_PlaybackStates_IsCompleted_EpisodeId",
                table: "PlaybackStates");

            _ = migrationBuilder.DropIndex(
                name: "IX_PlaybackStates_IsDeleted_DeletedAt",
                table: "PlaybackStates");

            _ = migrationBuilder.DropIndex(
                name: "IX_PlaybackStates_LastPlayedAt",
                table: "PlaybackStates");

            _ = migrationBuilder.DropIndex(
                name: "IX_LocalTracks_IsDeleted_DeletedAt",
                table: "LocalTracks");

            _ = migrationBuilder.DropIndex(
                name: "IX_Episodes_IsDeleted_DeletedAt",
                table: "Episodes");

            _ = migrationBuilder.DropIndex(
                name: "IX_Episodes_ReleaseDate",
                table: "Episodes");

            _ = migrationBuilder.DropIndex(
                name: "IX_Episodes_SeriesId_LocalFolderPath",
                table: "Episodes");

            _ = migrationBuilder.DropIndex(
                name: "IX_DialogSuppressions_IsDeleted_DeletedAt",
                table: "DialogSuppressions");

            _ = migrationBuilder.DropIndex(
                name: "IX_DialogSuppressions_Key",
                table: "DialogSuppressions");

            _ = migrationBuilder.DropIndex(
                name: "IX_DashboardPositions_Section_Position",
                table: "DashboardPositions");

            _ = migrationBuilder.DropIndex(
                name: "IX_CoverImages_EntityType_EntityId",
                table: "CoverImages");

            _ = migrationBuilder.DropIndex(
                name: "IX_CoverImages_IsDeleted_DeletedAt",
                table: "CoverImages");

            _ = migrationBuilder.CreateIndex(
                name: "IX_WatchedTitles_NormalizedTitle",
                table: "WatchedTitles",
                column: "NormalizedTitle",
                unique: true,
                filter: "NOT \"IsDeleted\"");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Series_IsDeleted_DeletedAt",
                table: "Series",
                columns: IsDeletedDeletedAtColumns,
                filter: "\"IsDeleted\"");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Series_IsFavorite_Title",
                table: "Series",
                columns: IsFavoriteTitleColumns,
                filter: "NOT \"IsDeleted\"");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Series_IsOnlineImported_Title",
                table: "Series",
                columns: IsOnlineImportedTitleColumns,
                filter: "NOT \"IsDeleted\"");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Series_IsSubscribed_Title",
                table: "Series",
                columns: IsSubscribedTitleColumns,
                filter: "NOT \"IsDeleted\"");

            _ = migrationBuilder.CreateIndex(
                name: "IX_SecureSettings_Key",
                table: "SecureSettings",
                column: "Key",
                unique: true,
                filter: "NOT \"IsDeleted\"");

            _ = migrationBuilder.CreateIndex(
                name: "IX_PlaybackStates_IsCompleted_EpisodeId",
                table: "PlaybackStates",
                columns: IsCompletedEpisodeIdColumns);

            _ = migrationBuilder.CreateIndex(
                name: "IX_PlaybackStates_IsDeleted_DeletedAt",
                table: "PlaybackStates",
                columns: IsDeletedDeletedAtColumns,
                filter: "\"IsDeleted\"");

            _ = migrationBuilder.CreateIndex(
                name: "IX_PlaybackStates_LastPlayedAt",
                table: "PlaybackStates",
                column: "LastPlayedAt",
                filter: "NOT \"IsDeleted\" AND LastPlayedAt IS NOT NULL");

            _ = migrationBuilder.CreateIndex(
                name: "IX_LocalTracks_IsDeleted_DeletedAt",
                table: "LocalTracks",
                columns: IsDeletedDeletedAtColumns,
                filter: "\"IsDeleted\"");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Episodes_IsDeleted_DeletedAt",
                table: "Episodes",
                columns: IsDeletedDeletedAtColumns,
                filter: "\"IsDeleted\"");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Episodes_ReleaseDate",
                table: "Episodes",
                column: "ReleaseDate",
                filter: "NOT \"IsDeleted\" AND ReleaseDate IS NOT NULL");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Episodes_SeriesId_LocalFolderPath",
                table: "Episodes",
                columns: SeriesIdLocalFolderPathColumns,
                filter: "NOT \"IsDeleted\"");

            _ = migrationBuilder.CreateIndex(
                name: "IX_DialogSuppressions_IsDeleted_DeletedAt",
                table: "DialogSuppressions",
                columns: IsDeletedDeletedAtColumns,
                filter: "\"IsDeleted\"");

            _ = migrationBuilder.CreateIndex(
                name: "IX_DialogSuppressions_Key",
                table: "DialogSuppressions",
                column: "Key",
                unique: true,
                filter: "NOT \"IsDeleted\"");

            _ = migrationBuilder.CreateIndex(
                name: "IX_DashboardPositions_Section_Position",
                table: "DashboardPositions",
                columns: SectionPositionColumns,
                filter: "NOT \"IsDeleted\"");

            _ = migrationBuilder.CreateIndex(
                name: "IX_CoverImages_EntityType_EntityId",
                table: "CoverImages",
                columns: EntityTypeEntityIdColumns,
                unique: true,
                filter: "NOT \"IsDeleted\"");

            _ = migrationBuilder.CreateIndex(
                name: "IX_CoverImages_IsDeleted_DeletedAt",
                table: "CoverImages",
                columns: IsDeletedDeletedAtColumns,
                filter: "\"IsDeleted\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropIndex(
                name: "IX_WatchedTitles_NormalizedTitle",
                table: "WatchedTitles");

            _ = migrationBuilder.DropIndex(
                name: "IX_Series_IsDeleted_DeletedAt",
                table: "Series");

            _ = migrationBuilder.DropIndex(
                name: "IX_Series_IsFavorite_Title",
                table: "Series");

            _ = migrationBuilder.DropIndex(
                name: "IX_Series_IsOnlineImported_Title",
                table: "Series");

            _ = migrationBuilder.DropIndex(
                name: "IX_Series_IsSubscribed_Title",
                table: "Series");

            _ = migrationBuilder.DropIndex(
                name: "IX_SecureSettings_Key",
                table: "SecureSettings");

            _ = migrationBuilder.DropIndex(
                name: "IX_PlaybackStates_IsCompleted_EpisodeId",
                table: "PlaybackStates");

            _ = migrationBuilder.DropIndex(
                name: "IX_PlaybackStates_IsDeleted_DeletedAt",
                table: "PlaybackStates");

            _ = migrationBuilder.DropIndex(
                name: "IX_PlaybackStates_LastPlayedAt",
                table: "PlaybackStates");

            _ = migrationBuilder.DropIndex(
                name: "IX_LocalTracks_IsDeleted_DeletedAt",
                table: "LocalTracks");

            _ = migrationBuilder.DropIndex(
                name: "IX_Episodes_IsDeleted_DeletedAt",
                table: "Episodes");

            _ = migrationBuilder.DropIndex(
                name: "IX_Episodes_ReleaseDate",
                table: "Episodes");

            _ = migrationBuilder.DropIndex(
                name: "IX_Episodes_SeriesId_LocalFolderPath",
                table: "Episodes");

            _ = migrationBuilder.DropIndex(
                name: "IX_DialogSuppressions_IsDeleted_DeletedAt",
                table: "DialogSuppressions");

            _ = migrationBuilder.DropIndex(
                name: "IX_DialogSuppressions_Key",
                table: "DialogSuppressions");

            _ = migrationBuilder.DropIndex(
                name: "IX_DashboardPositions_Section_Position",
                table: "DashboardPositions");

            _ = migrationBuilder.DropIndex(
                name: "IX_CoverImages_EntityType_EntityId",
                table: "CoverImages");

            _ = migrationBuilder.DropIndex(
                name: "IX_CoverImages_IsDeleted_DeletedAt",
                table: "CoverImages");

            _ = migrationBuilder.CreateIndex(
                name: "IX_WatchedTitles_NormalizedTitle",
                table: "WatchedTitles",
                column: "NormalizedTitle",
                unique: true,
                filter: "IsDeleted = 0");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Series_IsDeleted_DeletedAt",
                table: "Series",
                columns: IsDeletedDeletedAtColumns,
                filter: "IsDeleted = 1");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Series_IsFavorite_Title",
                table: "Series",
                columns: IsFavoriteTitleColumns,
                filter: "IsDeleted = 0");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Series_IsOnlineImported_Title",
                table: "Series",
                columns: IsOnlineImportedTitleColumns,
                filter: "IsDeleted = 0");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Series_IsSubscribed_Title",
                table: "Series",
                columns: IsSubscribedTitleColumns,
                filter: "IsDeleted = 0");

            _ = migrationBuilder.CreateIndex(
                name: "IX_SecureSettings_Key",
                table: "SecureSettings",
                column: "Key",
                unique: true,
                filter: "IsDeleted = 0");

            _ = migrationBuilder.CreateIndex(
                name: "IX_PlaybackStates_IsCompleted_EpisodeId",
                table: "PlaybackStates",
                columns: IsCompletedEpisodeIdColumns,
                filter: "IsDeleted = 0");

            _ = migrationBuilder.CreateIndex(
                name: "IX_PlaybackStates_IsDeleted_DeletedAt",
                table: "PlaybackStates",
                columns: IsDeletedDeletedAtColumns,
                filter: "IsDeleted = 1");

            _ = migrationBuilder.CreateIndex(
                name: "IX_PlaybackStates_LastPlayedAt",
                table: "PlaybackStates",
                column: "LastPlayedAt",
                filter: "IsDeleted = 0 AND LastPlayedAt IS NOT NULL");

            _ = migrationBuilder.CreateIndex(
                name: "IX_LocalTracks_IsDeleted_DeletedAt",
                table: "LocalTracks",
                columns: IsDeletedDeletedAtColumns,
                filter: "IsDeleted = 1");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Episodes_IsDeleted_DeletedAt",
                table: "Episodes",
                columns: IsDeletedDeletedAtColumns,
                filter: "IsDeleted = 1");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Episodes_ReleaseDate",
                table: "Episodes",
                column: "ReleaseDate",
                filter: "IsDeleted = 0 AND ReleaseDate IS NOT NULL");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Episodes_SeriesId_LocalFolderPath",
                table: "Episodes",
                columns: SeriesIdLocalFolderPathColumns,
                filter: "IsDeleted = 0");

            _ = migrationBuilder.CreateIndex(
                name: "IX_DialogSuppressions_IsDeleted_DeletedAt",
                table: "DialogSuppressions",
                columns: IsDeletedDeletedAtColumns,
                filter: "IsDeleted = 1");

            _ = migrationBuilder.CreateIndex(
                name: "IX_DialogSuppressions_Key",
                table: "DialogSuppressions",
                column: "Key",
                unique: true,
                filter: "IsDeleted = 0");

            _ = migrationBuilder.CreateIndex(
                name: "IX_DashboardPositions_Section_Position",
                table: "DashboardPositions",
                columns: SectionPositionColumns,
                filter: "IsDeleted = 0");

            _ = migrationBuilder.CreateIndex(
                name: "IX_CoverImages_EntityType_EntityId",
                table: "CoverImages",
                columns: EntityTypeEntityIdColumns,
                unique: true,
                filter: "IsDeleted = 0");

            _ = migrationBuilder.CreateIndex(
                name: "IX_CoverImages_IsDeleted_DeletedAt",
                table: "CoverImages",
                columns: IsDeletedDeletedAtColumns,
                filter: "IsDeleted = 1");
        }
    }
}
