using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoPlay.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackfillWatchedForFavorites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Reine Datenmigration, kein Schema-Wechsel: Favorit impliziert seit dieser Version
            // Überwachung. Bestände, die vor der Regel entstanden sind (neu eingelesene Serien
            // starten mit IsWatched = 0), hätten sonst dauerhaft favorisierte, aber unbeobachtete
            // Serien – und damit einen leeren Neuerscheinungen-Abschnitt ohne erkennbaren Grund.
            _ = migrationBuilder.Sql(
                "UPDATE Series SET IsWatched = 1 WHERE IsFavorite = 1 AND IsWatched = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Nicht umkehrbar: welche Serien vor dem Backfill unbeobachtet waren, ist nachträglich
            // nicht mehr feststellbar. Ein pauschales Zurücksetzen würde bewusst gesetzte
            // Überwachungen mit abräumen – deshalb bleibt der Rückweg absichtlich leer.
        }
    }
}
