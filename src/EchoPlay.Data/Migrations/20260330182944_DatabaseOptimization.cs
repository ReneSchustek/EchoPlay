using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class DatabaseOptimization : Migration
    {
        private static readonly string[] IsDeletedDeletedAt = ["IsDeleted", "DeletedAt"];
        private static readonly string[] IsFavoriteTitle = ["IsFavorite", "Title"];
        private static readonly string[] IsSubscribedTitle = ["IsSubscribed", "Title"];
        private static readonly string[] IsCompletedEpisodeId = ["IsCompleted", "EpisodeId"];
        private static readonly string[] EpisodeIdTrackNumber = ["EpisodeId", "TrackNumber"];
        private static readonly string[] SeriesIdLocalFolderPath = ["SeriesId", "LocalFolderPath"];
        private static readonly string[] SectionPosition = ["Section", "Position"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropIndex(
                name: "IX_LocalTracks_EpisodeId",
                table: "LocalTracks");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Series_AppleMusicArtistId",
                table: "Series",
                column: "AppleMusicArtistId",
                filter: "AppleMusicArtistId IS NOT NULL");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Series_IsDeleted_DeletedAt",
                table: "Series",
                columns: IsDeletedDeletedAt,
                filter: "IsDeleted = 1");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Series_IsFavorite_Title",
                table: "Series",
                columns: IsFavoriteTitle,
                filter: "IsDeleted = 0");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Series_IsSubscribed_Title",
                table: "Series",
                columns: IsSubscribedTitle,
                filter: "IsDeleted = 0");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Series_SpotifyArtistId",
                table: "Series",
                column: "SpotifyArtistId",
                filter: "SpotifyArtistId IS NOT NULL");

            _ = migrationBuilder.CreateIndex(
                name: "IX_PlaybackStates_IsCompleted_EpisodeId",
                table: "PlaybackStates",
                columns: IsCompletedEpisodeId,
                filter: "IsDeleted = 0");

            _ = migrationBuilder.CreateIndex(
                name: "IX_PlaybackStates_IsDeleted_DeletedAt",
                table: "PlaybackStates",
                columns: IsDeletedDeletedAt,
                filter: "IsDeleted = 1");

            _ = migrationBuilder.CreateIndex(
                name: "IX_LocalTracks_EpisodeId_TrackNumber",
                table: "LocalTracks",
                columns: EpisodeIdTrackNumber);

            _ = migrationBuilder.CreateIndex(
                name: "IX_LocalTracks_IsDeleted_DeletedAt",
                table: "LocalTracks",
                columns: IsDeletedDeletedAt,
                filter: "IsDeleted = 1");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Episodes_IsDeleted_DeletedAt",
                table: "Episodes",
                columns: IsDeletedDeletedAt,
                filter: "IsDeleted = 1");

            _ = migrationBuilder.CreateIndex(
                name: "IX_Episodes_SeriesId_LocalFolderPath",
                table: "Episodes",
                columns: SeriesIdLocalFolderPath,
                filter: "IsDeleted = 0");

            _ = migrationBuilder.CreateIndex(
                name: "IX_DashboardPositions_Section_Position",
                table: "DashboardPositions",
                columns: SectionPosition,
                filter: "IsDeleted = 0");

            _ = migrationBuilder.AddForeignKey(
                name: "FK_DashboardPositions_Series_SeriesId",
                table: "DashboardPositions",
                column: "SeriesId",
                principalTable: "Series",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            _ = migrationBuilder.DropForeignKey(
                name: "FK_DashboardPositions_Series_SeriesId",
                table: "DashboardPositions");

            _ = migrationBuilder.DropIndex(
                name: "IX_Series_AppleMusicArtistId",
                table: "Series");

            _ = migrationBuilder.DropIndex(
                name: "IX_Series_IsDeleted_DeletedAt",
                table: "Series");

            _ = migrationBuilder.DropIndex(
                name: "IX_Series_IsFavorite_Title",
                table: "Series");

            _ = migrationBuilder.DropIndex(
                name: "IX_Series_IsSubscribed_Title",
                table: "Series");

            _ = migrationBuilder.DropIndex(
                name: "IX_Series_SpotifyArtistId",
                table: "Series");

            _ = migrationBuilder.DropIndex(
                name: "IX_PlaybackStates_IsCompleted_EpisodeId",
                table: "PlaybackStates");

            _ = migrationBuilder.DropIndex(
                name: "IX_PlaybackStates_IsDeleted_DeletedAt",
                table: "PlaybackStates");

            _ = migrationBuilder.DropIndex(
                name: "IX_LocalTracks_EpisodeId_TrackNumber",
                table: "LocalTracks");

            _ = migrationBuilder.DropIndex(
                name: "IX_LocalTracks_IsDeleted_DeletedAt",
                table: "LocalTracks");

            _ = migrationBuilder.DropIndex(
                name: "IX_Episodes_IsDeleted_DeletedAt",
                table: "Episodes");

            _ = migrationBuilder.DropIndex(
                name: "IX_Episodes_SeriesId_LocalFolderPath",
                table: "Episodes");

            _ = migrationBuilder.DropIndex(
                name: "IX_DashboardPositions_Section_Position",
                table: "DashboardPositions");

            _ = migrationBuilder.CreateIndex(
                name: "IX_LocalTracks_EpisodeId",
                table: "LocalTracks",
                column: "EpisodeId");
        }
    }
}
