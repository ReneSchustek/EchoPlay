using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Data.Sqlite;
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
        EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory) : ICoverCopyService
    {
        private readonly EchoPlayDbContext _context = context;
        private readonly ILogger _logger = loggerFactory.CreateLogger("CoverCopyService");

        // Gemeinsamer Kopf aller drei Kopier-Stufen: erzeugt eine neue UUIDv4 und
        // selektiert das Quell-Cover (ci) für die Ziel-Episode (tgt).
        private const string InsertCoverHeader = """
            INSERT OR IGNORE INTO CoverImages (Id, EntityType, EntityId, ImageData, SourceUrl, CreatedAt, IsDeleted)
            SELECT
                lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))),
                'Episode', tgt.Id, ci.ImageData, ci.SourceUrl, datetime('now'), 0
            FROM Episodes tgt
            """;

        // Gemeinsame Fußzeile: begrenzt auf die Ziel-Serie und überspringt Episoden,
        // die bereits ein Cover besitzen.
        private const string InsertCoverFooter = """
            WHERE tgt.SeriesId = @targetSeriesId
                AND tgt.IsDeleted = 0
                AND tgt.EpisodeNumber IS NOT NULL
                AND NOT EXISTS (
                    SELECT 1 FROM CoverImages x
                    WHERE x.EntityType = 'Episode' AND x.EntityId = tgt.Id)
            """;

        // Stufe 1: gleicher Serienname + Folgennummer + Titel (exakter Match).
        private const string JoinByExact = """
            INNER JOIN Episodes src
                ON src.EpisodeNumber = tgt.EpisodeNumber
                AND src.Title = tgt.Title COLLATE NOCASE
                AND src.IsDeleted = 0
            INNER JOIN Series s
                ON src.SeriesId = s.Id
                AND s.Title = @seriesTitle COLLATE NOCASE
            INNER JOIN CoverImages ci
                ON ci.EntityType = 'Episode' AND ci.EntityId = src.Id
            """;

        // Stufe 2: gleicher Serienname + Folgennummer (Titel egal).
        private const string JoinByNumber = """
            INNER JOIN Episodes src
                ON src.EpisodeNumber = tgt.EpisodeNumber
                AND src.IsDeleted = 0
            INNER JOIN Series s
                ON src.SeriesId = s.Id
                AND s.Title = @seriesTitle COLLATE NOCASE
            INNER JOIN CoverImages ci
                ON ci.EntityType = 'Episode' AND ci.EntityId = src.Id
            """;

        // Stufe 3: serienübergreifend per Folgennummer + Titel-Schlagwort (LIKE),
        // nur von lokalen Serien (LocalFolderPath gesetzt) mit nicht-leerem Bild.
        private const string JoinByKeyword = """
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
            """;

        /// <inheritdoc/>
        /// <param name="targetSeriesId">Parameter targetSeriesId.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<int> CopyFromMatchingEpisodesAsync(Guid targetSeriesId, CancellationToken cancellationToken = default)
        {
            Series? targetSeries = await _context.Series

                .FirstOrDefaultAsync(s => s.Id == targetSeriesId, cancellationToken)
                .ConfigureAwait(false);

            if (targetSeries is null) return 0;

            string seriesTitle = targetSeries.Title;

            // Prüfen ob überhaupt Episoden ohne Cover existieren
            bool hasMissing = await _context.Episodes

                .AnyAsync(e => e.SeriesId == targetSeriesId
                    && !_context.CoverImages.Any(ci =>
                        ci.EntityType == CoverEntityTypes.Episode && ci.EntityId == e.Id), cancellationToken)
                .ConfigureAwait(false);

            if (!hasMissing) return 0;

            // Stufe 1: exakter Match (Serienname + Nummer + Titel).
            int byExact = await InsertMatchingCoversAsync(JoinByExact, targetSeriesId, seriesTitle, cancellationToken).ConfigureAwait(false);

            // Stufe 2: Serienname + Nummer (Titel egal).
            int byNumber = await InsertMatchingCoversAsync(JoinByNumber, targetSeriesId, seriesTitle, cancellationToken).ConfigureAwait(false);

            // Stufe 3: serienübergreifend per Nummer + Titel-Schlagwort. Greift bei
            // abweichendem Serientitel (z.B. "Die drei Fragezeichen" vs. "Die drei ???").
            // Beispiel: "Der Super-Papagei" ist enthalten in "001/und der Super-Papagei".
            int byKeyword = await InsertMatchingCoversAsync(JoinByKeyword, targetSeriesId, seriesTitle: null, cancellationToken).ConfigureAwait(false);

            int total = byExact + byNumber + byKeyword;

            if (total > 0)
            {
                _logger.Info(
                    "Cover-Kopie für \"{SeriesTitle}\": {Total} kopiert ({ByExact} exakt, {ByNumber} per Nummer, {ByKeyword} per Schlagwort).",
                    seriesTitle, total, byExact, byNumber, byKeyword);
            }

            return total;
        }

        /// <summary>
        /// Führt eine Kopier-Stufe aus: setzt das JOIN-Fragment zwischen den gemeinsamen
        /// Kopf und die gemeinsame Fußzeile und liefert die Anzahl kopierter Cover.
        /// </summary>
        /// <param name="joinClause">Das stufenspezifische JOIN-Fragment (vertrauenswürdige Konstante).</param>
        /// <param name="targetSeriesId">Die Ziel-Serie, deren Episoden Cover erhalten sollen.</param>
        /// <param name="seriesTitle">Der Serientitel für den Namensabgleich; <c>null</c>, wenn das Fragment ihn nicht nutzt.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Die Anzahl der eingefügten Cover-Datensätze.</returns>
        private Task<int> InsertMatchingCoversAsync(
            string joinClause,
            Guid targetSeriesId,
            string? seriesTitle,
            CancellationToken cancellationToken)
        {
            string sql = string.Join('\n', InsertCoverHeader, joinClause, InsertCoverFooter);

            List<SqliteParameter> parameters = [new SqliteParameter("@targetSeriesId", targetSeriesId)];
            if (seriesTitle is not null)
            {
                parameters.Add(new SqliteParameter("@seriesTitle", seriesTitle));
            }

            return _context.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
        }
    }
}
