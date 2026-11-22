using EchoPlay.Data.Entities.Library;

namespace EchoPlay.Data.Services.Interfaces
{
    /// <summary>
    /// Zugriff auf den Veröffentlichungskalender: die im Vormonat, im laufenden Monat und
    /// später erscheinenden Folgen aller vorhandenen Serien.
    /// </summary>
    public interface IReleaseCalendarDataService
    {
        /// <summary>
        /// Liefert die Einträge eines Zeitraums samt Serie, nach Erscheinungszeitpunkt
        /// aufsteigend und bei gleichem Zeitpunkt nach Serientitel.
        /// </summary>
        /// <param name="fromUtc">Beginn des Zeitraums (UTC, eingeschlossen).</param>
        /// <param name="toUtc">Ende des Zeitraums (UTC, ausgeschlossen).</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Die Einträge des Zeitraums; Einträge gelöschter Serien fehlen.</returns>
        Task<IReadOnlyList<ReleaseCalendarEntry>> GetBetweenAsync(
            DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

        /// <summary>Liefert den spätesten Erscheinungszeitpunkt im Kalender.</summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Der späteste Zeitpunkt, oder <c>null</c> bei leerem Kalender.</returns>
        Task<DateTime?> GetLatestReleaseDateAsync(CancellationToken cancellationToken = default);

        /// <summary>Liefert das Ende des letzten vollständigen Aufbaus.</summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Der Zeitpunkt (UTC), oder <c>null</c>, wenn noch kein Aufbau fertig wurde.</returns>
        Task<DateTime?> GetLastRefreshAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Vermerkt einen vollständigen Aufbau. Geschrieben wird nur diese eine Spalte, damit
        /// gleichzeitig gespeicherte Einstellungen nicht überschrieben werden.
        /// </summary>
        /// <param name="refreshedAtUtc">Ende des Aufbaus (UTC).</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task SetLastRefreshAsync(DateTime refreshedAtUtc, CancellationToken cancellationToken = default);

        /// <summary>
        /// Legt neue Einträge an und aktualisiert vorhandene anhand der Sammlungs-Kennung.
        /// Ein verschobener Termin oder ein geänderter Titel kommt so mit der nächsten
        /// Prüfung an.
        /// </summary>
        /// <param name="entries">Die zu schreibenden Einträge.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task UpsertRangeAsync(IReadOnlyList<ReleaseCalendarEntry> entries, CancellationToken cancellationToken = default);

        /// <summary>Entfernt alle Einträge, die vor dem Stichtag erschienen sind.</summary>
        /// <param name="cutoffUtc">Stichtag (UTC); alles davor fällt weg.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Anzahl der entfernten Einträge.</returns>
        Task<int> RemoveOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);
    }
}
