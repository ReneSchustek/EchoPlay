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
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == targetSeriesId)
                .ConfigureAwait(false);

            if (targetSeries is null) return 0;

            string seriesTitle = targetSeries.Title;

            // Prüfen ob überhaupt Episoden ohne Cover existieren
            bool hasMissing = await _context.Episodes
                .AsNoTracking()
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

            if (total > 0)
            {
                _logger.Info($"Cover-Kopie für \"{seriesTitle}\": {total} kopiert " +
                    $"({byExact} exakt, {byNumber} per Nummer).");
            }

            return total;
        }
    }
}
