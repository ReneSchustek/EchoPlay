using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.Data.Services
{
    /// <summary>
    /// Kopiert Cover-Daten zwischen Entities komplett in SQL über die CoverImages-Tabelle.
    /// Kein Blob wird nach C# geladen – die gesamte Kopie läuft in der Datenbank.
    /// Dreistufig: Episoden-Cover (exakt) → Episoden-Cover (Nummer) → Serien-Cover (Fallback).
    /// </summary>
    public sealed class CoverCopyService(
        EchoPlayDbContext context,
        ILoggerFactory loggerFactory) : ICoverCopyService
    {
        private readonly EchoPlayDbContext _context = context;
        private readonly ILogger _logger = loggerFactory.CreateLogger("CoverCopyService");

        /// <inheritdoc/>
        public async Task<int> CopyFromMatchingEpisodesAsync(Guid targetSeriesId)
        {
            Series? targetSeries = await _context.Series

                .FirstOrDefaultAsync(s => s.Id == targetSeriesId)
                .ConfigureAwait(false);

            if (targetSeries is null) return 0;

            string seriesTitle = targetSeries.Title;

            // Prüfen ob überhaupt Episoden ohne Cover existieren
            bool hasMissing = await _context.Episodes

                .AnyAsync(e => e.SeriesId == targetSeriesId
                    && !_context.CoverImages.Any(ci =>
                        ci.EntityType == CoverEntityTypes.Episode && ci.EntityId == e.Id))
                .ConfigureAwait(false);

            if (!hasMissing) return 0;

            int total = 0;

            // ── Stufe 1: Episoden-Cover per Serienname + Nummer + Titel ─────────
            // Sucht in CoverImages nach Episoden-Covern von Serien mit gleichem Titel
            int byExact = await _context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT OR IGNORE INTO CoverImages (Id, EntityType, EntityId, ImageData, SourceUrl, CreatedAt, IsDeleted)
                SELECT
                    lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))),
                    'Episode', tgt.Id, ci.ImageData, ci.SourceUrl, datetime('now'), 0
                FROM Episodes tgt
                INNER JOIN Episodes src
                    ON src.EpisodeNumber = tgt.EpisodeNumber
                    AND src.Title = tgt.Title COLLATE NOCASE
                    AND src.IsDeleted = 0
                INNER JOIN Series s
                    ON src.SeriesId = s.Id
                    AND s.Title = {seriesTitle} COLLATE NOCASE
                INNER JOIN CoverImages ci
                    ON ci.EntityType = 'Episode' AND ci.EntityId = src.Id
                WHERE tgt.SeriesId = {targetSeriesId}
                    AND tgt.IsDeleted = 0
                    AND tgt.EpisodeNumber IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1 FROM CoverImages x
                        WHERE x.EntityType = 'Episode' AND x.EntityId = tgt.Id)
                """).ConfigureAwait(false);

            total += byExact;

            // ── Stufe 2: Episoden-Cover per Serienname + Nummer ─────────────────
            int byNumber = await _context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT OR IGNORE INTO CoverImages (Id, EntityType, EntityId, ImageData, SourceUrl, CreatedAt, IsDeleted)
                SELECT
                    lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))),
                    'Episode', tgt.Id, ci.ImageData, ci.SourceUrl, datetime('now'), 0
                FROM Episodes tgt
                INNER JOIN Episodes src
                    ON src.EpisodeNumber = tgt.EpisodeNumber
                    AND src.IsDeleted = 0
                INNER JOIN Series s
                    ON src.SeriesId = s.Id
                    AND s.Title = {seriesTitle} COLLATE NOCASE
                INNER JOIN CoverImages ci
                    ON ci.EntityType = 'Episode' AND ci.EntityId = src.Id
                WHERE tgt.SeriesId = {targetSeriesId}
                    AND tgt.IsDeleted = 0
                    AND tgt.EpisodeNumber IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1 FROM CoverImages x
                        WHERE x.EntityType = 'Episode' AND x.EntityId = tgt.Id)
                """).ConfigureAwait(false);

            total += byNumber;

            // ── Stufe 3: Episodennummer + Titel-Schlagwort serienübergreifend ───
            // Greift bei abweichendem Serientitel (z.B. "Die drei Fragezeichen" vs.
            // "Die drei ???"). Matcht per Episodennummer UND prüft ob der eine
            // Episodentitel im anderen enthalten ist (LIKE, case-insensitive).
            // Beispiel: "Der Super-Papagei" ist enthalten in "001/und der Super-Papagei".
            // Nur von lokalen Serien (LocalFolderPath gesetzt).
            int byKeyword = await _context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT OR IGNORE INTO CoverImages (Id, EntityType, EntityId, ImageData, SourceUrl, CreatedAt, IsDeleted)
                SELECT
                    lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))),
                    'Episode', tgt.Id, ci.ImageData, ci.SourceUrl, datetime('now'), 0
                FROM Episodes tgt
                INNER JOIN Episodes src
                    ON src.EpisodeNumber = tgt.EpisodeNumber
                    AND src.Id != tgt.Id
                    AND src.IsDeleted = 0
                    AND (tgt.Title LIKE '%' || src.Title || '%'
                         OR src.Title LIKE '%' || tgt.Title || '%')
                INNER JOIN Series srcSeries
                    ON src.SeriesId = srcSeries.Id
                    AND srcSeries.LocalFolderPath IS NOT NULL
                INNER JOIN CoverImages ci
                    ON ci.EntityType = 'Episode' AND ci.EntityId = src.Id
                    AND length(ci.ImageData) > 0
                WHERE tgt.SeriesId = {targetSeriesId}
                    AND tgt.IsDeleted = 0
                    AND tgt.EpisodeNumber IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1 FROM CoverImages x
                        WHERE x.EntityType = 'Episode' AND x.EntityId = tgt.Id)
                """).ConfigureAwait(false);

            total += byKeyword;

            if (total > 0)
            {
                _logger.Info($"Cover-Kopie für \"{seriesTitle}\": {total} kopiert " +
                    $"({byExact} exakt, {byNumber} per Nummer, {byKeyword} per Schlagwort).");
            }

            return total;
        }
    }
}
