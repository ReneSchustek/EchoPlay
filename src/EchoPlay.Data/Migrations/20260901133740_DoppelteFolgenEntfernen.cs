using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class DoppelteFolgenEntfernen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Reine Datenbereinigung. Der Re-Import legte bisher jede Folge des Anbieters neu an,
            // statt nur die fehlenden – wer ihn neben einem laufenden Erstimport auslöste, bekam
            // den gesamten Bestand ein zweites Mal. Betroffene Serien zeigten die Folgen doppelt.
            //
            // Behalten wird je Serie und Anbieter-Kennung der älteste Eintrag. Alles, was an den
            // Dubletten hängt – Wiedergabestand, Cover, lokale Titel – wandert vorher auf ihn über,
            // damit die Bereinigung keine gehörten Folgen und keine Bilder kostet.
            _ = migrationBuilder.Sql("""
                CREATE TEMP TABLE DoppelteFolgen AS
                SELECT e.Id AS DubletteId, k.BehaltenId AS BehaltenId
                FROM Episodes e
                JOIN (
                    SELECT SeriesId, Kennung, Id AS BehaltenId
                    FROM (
                        SELECT SeriesId,
                               COALESCE(AppleMusicAlbumId, SpotifyAlbumId) AS Kennung,
                               Id,
                               ROW_NUMBER() OVER (
                                   PARTITION BY SeriesId, COALESCE(AppleMusicAlbumId, SpotifyAlbumId)
                                   ORDER BY CreatedAt, rowid) AS Rang
                        FROM Episodes
                        WHERE IsDeleted = 0
                          AND COALESCE(AppleMusicAlbumId, SpotifyAlbumId) IS NOT NULL
                    )
                    WHERE Rang = 1
                ) k ON k.SeriesId = e.SeriesId
                   AND k.Kennung = COALESCE(e.AppleMusicAlbumId, e.SpotifyAlbumId)
                WHERE e.IsDeleted = 0
                  AND COALESCE(e.AppleMusicAlbumId, e.SpotifyAlbumId) IS NOT NULL
                  AND e.Id <> k.BehaltenId;
                """);

            // War eine Dublette als gehört markiert, gilt das für den verbleibenden Eintrag.
            _ = migrationBuilder.Sql("""
                UPDATE PlaybackStates
                SET IsCompleted = 1
                WHERE IsCompleted = 0
                  AND EpisodeId IN (
                      SELECT d.BehaltenId
                      FROM DoppelteFolgen d
                      JOIN PlaybackStates p ON p.EpisodeId = d.DubletteId
                      WHERE p.IsCompleted = 1);
                """);

            // Hat der verbleibende Eintrag noch gar keinen Wiedergabestand, wandert der weiteste
            // Stand seiner Dubletten auf ihn über. Genau einer – auf PlaybackStates.EpisodeId
            // liegt ein UNIQUE-Index, ein zweiter Stand wäre ein Fehlschlag.
            _ = migrationBuilder.Sql("""
                UPDATE PlaybackStates
                SET EpisodeId = (
                    SELECT d.BehaltenId FROM DoppelteFolgen d WHERE d.DubletteId = PlaybackStates.EpisodeId)
                WHERE Id IN (
                    SELECT (
                        SELECT p.Id
                        FROM PlaybackStates p
                        JOIN DoppelteFolgen d2 ON d2.DubletteId = p.EpisodeId
                        WHERE d2.BehaltenId = d.BehaltenId
                        ORDER BY p.IsCompleted DESC, p.LastPlayedAt DESC
                        LIMIT 1)
                    FROM DoppelteFolgen d
                    WHERE NOT EXISTS (
                        SELECT 1 FROM PlaybackStates k WHERE k.EpisodeId = d.BehaltenId)
                    GROUP BY d.BehaltenId);
                """);

            _ = migrationBuilder.Sql("""
                DELETE FROM PlaybackStates WHERE EpisodeId IN (SELECT DubletteId FROM DoppelteFolgen);
                """);

            // Lokale Titel hängen an der Folge, nicht am Anbieter – sie ziehen vollständig um.
            _ = migrationBuilder.Sql("""
                UPDATE LocalTracks
                SET EpisodeId = (
                    SELECT d.BehaltenId FROM DoppelteFolgen d WHERE d.DubletteId = LocalTracks.EpisodeId)
                WHERE EpisodeId IN (SELECT DubletteId FROM DoppelteFolgen);
                """);

            // Ein Cover, das nur an einer Dublette hängt, wandert auf den verbleibenden Eintrag.
            // Auch hier genau eines: (EntityType, EntityId) ist für aktive Zeilen eindeutig.
            _ = migrationBuilder.Sql("""
                UPDATE CoverImages
                SET EntityId = (
                    SELECT d.BehaltenId FROM DoppelteFolgen d WHERE d.DubletteId = CoverImages.EntityId)
                WHERE Id IN (
                    SELECT (
                        SELECT c.Id
                        FROM CoverImages c
                        JOIN DoppelteFolgen d2 ON d2.DubletteId = c.EntityId
                        WHERE c.EntityType = 'Episode' AND c.IsDeleted = 0 AND d2.BehaltenId = d.BehaltenId
                        ORDER BY c.CreatedAt
                        LIMIT 1)
                    FROM DoppelteFolgen d
                    WHERE NOT EXISTS (
                        SELECT 1 FROM CoverImages k
                        WHERE k.EntityType = 'Episode' AND k.IsDeleted = 0 AND k.EntityId = d.BehaltenId)
                    GROUP BY d.BehaltenId);
                """);

            _ = migrationBuilder.Sql("""
                DELETE FROM CoverImages
                WHERE EntityType = 'Episode'
                  AND EntityId IN (SELECT DubletteId FROM DoppelteFolgen);
                """);

            _ = migrationBuilder.Sql("""
                DELETE FROM Episodes WHERE Id IN (SELECT DubletteId FROM DoppelteFolgen);
                """);

            _ = migrationBuilder.Sql("DROP TABLE DoppelteFolgen;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nicht umkehrbar: Die überzähligen Folgen sind physisch entfernt. Der Weg zurück
            // führt über den Snapshot, den der DatabaseInitializer vor der Migration anlegt.
        }
    }
}
