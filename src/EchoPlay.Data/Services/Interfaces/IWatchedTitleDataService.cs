namespace EchoPlay.Data.Services.Interfaces
{
    /// <summary>
    /// Verwaltet die Merkliste überwachter Serientitel.
    /// </summary>
    /// <remarks>
    /// Eigene Schnittstelle statt Anbau an <see cref="ISeriesDataService"/>: Die Merkliste ist ein
    /// eigenes Aggregat. Sie überlebt bewusst das Leeren der Mediathek — dabei verschwinden die
    /// <c>Series</c>-Zeilen physisch, die gemerkten Titel bleiben und geben neu eingelesenen
    /// Serien ihre Überwachung zurück.
    /// </remarks>
    public interface IWatchedTitleDataService
    {
        /// <summary>
        /// Liefert alle gemerkten Titel in normalisierter Form.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>
        /// Eine Menge — die Aufrufer schlagen darin pro gescanntem Ordner nach, eine Liste
        /// würde die Schleife unbemerkt quadratisch machen.
        /// </returns>
        Task<IReadOnlySet<string>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Merkt einen Serientitel. Ist er bereits gemerkt, passiert nichts.
        /// </summary>
        /// <param name="title">Der Serientitel in Originalschreibweise.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task RememberAsync(string title, CancellationToken cancellationToken = default);

        /// <summary>
        /// Entfernt einen gemerkten Titel — der Nutzer hat die Überwachung bewusst abgeschaltet.
        /// </summary>
        /// <param name="title">Der Serientitel in Originalschreibweise.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task ForgetAsync(string title, CancellationToken cancellationToken = default);

        /// <summary>
        /// Trägt alle Titel überwachter Serien nach, die noch nicht in der Merkliste stehen.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Anzahl der neu übernommenen Titel.</returns>
        Task<int> SyncFromWatchedSeriesAsync(CancellationToken cancellationToken = default);
    }
}
