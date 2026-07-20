using EchoPlay.Data.Entities.Library;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.Data.Services.Interfaces
{
    /// <summary>
    /// Definiert den Zugriff auf persistierte Episoden innerhalb von Hörspielserien.
    /// </summary>
    public interface IEpisodeDataService
    {
        /// <summary>
        /// Liefert alle Episoden einer bestimmten Serie.
        /// Die Episoden werden standardmäßig nach Episodennummer und anschließend nach Titel sortiert.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <param name="seriesId">Parameter seriesId.</param>
        Task<IReadOnlyList<Episode>> GetBySeriesIdAsync(Guid seriesId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Liefert Episodenzähler für mehrere Serien in einer einzigen Datenbankabfrage.
        /// Ersetzt den N+1-Zugriff beim Laden der lokalen Mediathek: statt einer Abfrage
        /// pro Serie wird eine gruppierte SQL-Query für alle übergebenen Serien ausgeführt.
        /// </summary>
        /// <param name="seriesIds">IDs der Serien, für die Zähler benötigt werden.</param>
        /// <returns>
        /// Dictionary von SeriesId auf (Total, Local)-Tupel.
        /// <c>Total</c> = Gesamtanzahl der nicht gelöschten Episoden,
        /// <c>Local</c> = Episoden mit mindestens einem gescannten lokalen Ordner.
        /// Serien ohne Episoden tauchen nicht im Dictionary auf – der Aufrufer bekommt (0, 0).
        /// </returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<IReadOnlyDictionary<Guid, (int Total, int Local)>> GetEpisodeCountsForSeriesAsync(IReadOnlyList<Guid> seriesIds, CancellationToken cancellationToken = default);

        /// <summary>
        /// Liefert alle Episoden mehrerer Serien in einer einzigen Datenbankabfrage.
        /// Ersetzt den N+1-Zugriff: statt einer Abfrage pro Serie wird ein einziger Query
        /// mit <c>WHERE SeriesId IN (...)</c> ausgeführt.
        /// </summary>
        /// <param name="seriesIds">IDs der Serien, deren Episoden geladen werden sollen.</param>
        /// <returns>Alle Episoden der angegebenen Serien, unsortiert.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<IReadOnlyList<Episode>> GetBySeriesIdsAsync(IReadOnlyList<Guid> seriesIds, CancellationToken cancellationToken = default);

        /// <summary>
        /// Liefert eine Episode anhand ihrer eindeutigen ID.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <param name="id">Parameter id.</param>
        Task<Episode?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Liefert mehrere Episoden anhand ihrer IDs in einer einzigen Datenbankabfrage
        /// (<c>WHERE Id IN (...)</c>) und mappt das Ergebnis auf ein Dictionary.
        /// Ersetzt den N+1-Zugriff im Dashboard-Aufbau: eine Abfrage pro
        /// Wiedergabestand wird zu einer Abfrage pro Batch.
        /// </summary>
        /// <param name="ids">IDs der zu ladenden Episoden. Duplikate werden toleriert.</param>
        /// <returns>
        /// Dictionary von Episoden-ID auf <see cref="Episode"/>. Nicht gefundene IDs fehlen
        /// im Dictionary; bei leerer Eingabe wird ein leeres Dictionary geliefert.
        /// </returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<IReadOnlyDictionary<Guid, Episode>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);

        /// <summary>
        /// Fügt eine neue Episode dauerhaft hinzu.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <param name="episode">Parameter episode.</param>
        Task AddAsync(Episode episode, CancellationToken cancellationToken = default);

        /// <summary>
        /// Fügt mehrere neue Episoden in einem einzigen <c>SaveChangesAsync</c>-Aufruf hinzu.
        /// Ersetzt die Schleife mit einzelnen <see cref="AddAsync"/>-Aufrufen beim Import großer
        /// Serien (200+ Episoden = 200+ DB-Roundtrips → 1 Roundtrip).
        /// </summary>
        /// <param name="episodes">Die zu persistierenden Episoden.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task AddRangeAsync(IReadOnlyList<Episode> episodes, CancellationToken cancellationToken = default);

        /// <summary>
        /// Aktualisiert eine bestehende Episode.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <param name="episode">Parameter episode.</param>
        Task UpdateAsync(Episode episode, CancellationToken cancellationToken = default);

        /// <summary>
        /// Aktualisiert mehrere bestehende Episoden in einem einzigen <c>SaveChangesAsync</c>-Aufruf.
        /// Wird beim Delta-Reimport genutzt, wenn nur Cover-URLs nachgezogen werden müssen, ohne
        /// pro Episode einen eigenen Roundtrip auszulösen.
        /// </summary>
        /// <param name="episodes">Die zu aktualisierenden Episoden.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task UpdateRangeAsync(IReadOnlyList<Episode> episodes, CancellationToken cancellationToken = default);

        /// <summary>
        /// Liefert alle Episoden einer Serie, für die noch kein lokaler Ordner zugeordnet wurde.
        /// Dies sind Episoden, die in der Datenbank bekannt sind (z.B. durch Online-Import),
        /// aber beim letzten Scan nicht auf der Festplatte gefunden wurden.
        /// </summary>
        /// <param name="seriesId">Die eindeutige ID der Serie.</param>
        /// <returns>
        /// Episoden ohne <c>LocalFolderPath</c>, sortiert nach Episodennummer und Titel.
        /// Gibt eine leere Liste zurück, wenn alle Episoden lokal vorhanden sind.
        /// </returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<IReadOnlyList<Episode>> GetMissingLocalEpisodesAsync(Guid seriesId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Ermittelt die höchste Episodennummer aller lokal vorhandenen Episoden einer Serie.
        /// Wird auf dem Dashboard verwendet, um zu entscheiden, welche Folgen als „Neuerscheinung"
        /// gelten: Nur Episoden mit einer höheren Nummer als dieser Wert sind wirklich neu.
        /// </summary>
        /// <param name="seriesId">Die eindeutige ID der Serie.</param>
        /// <returns>
        /// Die höchste Episodennummer mit zugeordnetem lokalen Ordner,
        /// oder <see langword="null"/> wenn keine lokale Episode existiert.
        /// </returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<int?> GetHighestLocalEpisodeNumberAsync(Guid seriesId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Setzt den Zeitstempel der letzten Cover-Suche.
        /// Wird nach jeder automatischen Suche gesetzt, unabhängig vom Ergebnis.
        /// Verhindert wiederholtes Durchsuchen bei Episoden ohne Treffer (Cooldown).
        /// </summary>
        /// <param name="episodeId">Die ID der Episode.</param>
        /// <param name="checkedAt">Zeitpunkt der Prüfung (UTC).</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task SetCoverLastCheckedAsync(Guid episodeId, DateTime checkedAt, CancellationToken cancellationToken = default);

        /// <summary>
        /// Markiert eine Episode als gelöscht (Soft-Delete).
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <param name="id">Parameter id.</param>
        Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    }
}
