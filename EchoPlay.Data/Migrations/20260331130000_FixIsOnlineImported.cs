using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixIsOnlineImported : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Korrektur: Die vorherige Migration hat IsOnlineImported zu breit gesetzt.
            // Serien mit gecachter AppleMusicArtistId (vom OnlineEpisodeChecker) sind
            // keine echten Online-Imports – nur Serien ohne LocalFolderPath sind es.
            // Erst alles zurücksetzen, dann nur die echten Online-Imports markieren.
            _ = migrationBuilder.Sql("""
                UPDATE Series SET IsOnlineImported = 0;

                UPDATE Series
                SET IsOnlineImported = 1
                WHERE (SpotifyArtistId IS NOT NULL OR AppleMusicArtistId IS NOT NULL)
                  AND LocalFolderPath IS NULL
                  AND IsDeleted = 0
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rückgängig: wieder alle Serien mit Provider-ID als Online markieren
            _ = migrationBuilder.Sql("""
                UPDATE Series
                SET IsOnlineImported = 1
                WHERE (SpotifyArtistId IS NOT NULL OR AppleMusicArtistId IS NOT NULL)
                  AND IsDeleted = 0
                """);
        }
    }
}
