using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class CleanupTrackEpisodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bereinigung: Alle Episoden von online-importierten Serien löschen.
            // Der bisherige Import hat pro Track eine Episode angelegt (N Tracks = N Episoden).
            // Ab jetzt wird pro Album eine Episode angelegt (1 Album = 1 Episode).
            // Die Episoden werden beim nächsten Import der Serie automatisch neu angelegt.
            // PlaybackStates werden per Cascade mitgelöscht (FK Episode → PlaybackState).
            migrationBuilder.Sql("""
                DELETE FROM PlaybackStates
                WHERE EpisodeId IN (
                    SELECT e.Id FROM Episodes e
                    INNER JOIN Series s ON e.SeriesId = s.Id
                    WHERE s.IsOnlineImported = 1
                );

                DELETE FROM Episodes
                WHERE SeriesId IN (
                    SELECT Id FROM Series WHERE IsOnlineImported = 1
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rückgängig nicht möglich – gelöschte Episoden können nicht wiederhergestellt werden.
            // Die Serien bleiben erhalten und können neu importiert werden.
        }
    }
}
