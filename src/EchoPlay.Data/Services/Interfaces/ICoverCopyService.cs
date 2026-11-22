namespace EchoPlay.Data.Services.Interfaces
{
    /// <summary>
    /// Kopiert Cover-Daten zwischen Episoden auf Datenbankebene. Die Kopie läuft als SQL in
    /// der Datenbank, damit keine Bilddaten in den Speicher der Anwendung geladen werden.
    /// </summary>
    public interface ICoverCopyService
    {
        /// <summary>
        /// Kopiert Cover aus vorhandenen Episoden auf Episoden der Zielserie, die noch kein
        /// Cover haben. Reihenfolge: gleicher Serientitel mit Nummer und Folgentitel, dann
        /// gleicher Serientitel mit Nummer, zuletzt serienübergreifend Nummer und Teil des Titels.
        /// </summary>
        /// <param name="targetSeriesId">ID der Serie, deren Episoden Cover erhalten sollen.</param>
        /// <returns>Anzahl der kopierten Cover.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<int> CopyFromMatchingEpisodesAsync(Guid targetSeriesId, CancellationToken cancellationToken = default);
    }
}
