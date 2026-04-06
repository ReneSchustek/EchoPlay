using System;
using System.Threading.Tasks;

namespace EchoPlay.Data.Services.Interfaces
{
    /// <summary>
    /// Kopiert Cover-Daten zwischen Episoden auf Datenbankebene.
    /// Verwendet Raw SQL, um DbContext-Tracking-Konflikte zu vermeiden,
    /// die bei großen Batch-Operationen über EF Core entstehen.
    /// </summary>
    public interface ICoverCopyService
    {
        /// <summary>
        /// Kopiert Cover aus vorhandenen Episoden (gleicher Serientitel) auf Episoden
        /// der Zielserie, die noch kein Cover haben.
        /// Matching-Reihenfolge: Folgennummer → exakter Titel → Teilstring-Titel.
        /// </summary>
        /// <param name="targetSeriesId">ID der Serie, deren Episoden Cover erhalten sollen.</param>
        /// <returns>Anzahl der kopierten Cover.</returns>
        Task<int> CopyFromMatchingEpisodesAsync(Guid targetSeriesId);
    }
}
