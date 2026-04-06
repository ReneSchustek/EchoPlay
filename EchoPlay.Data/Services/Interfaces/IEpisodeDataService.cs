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
        Task<IReadOnlyList<Episode>> GetBySeriesIdAsync(Guid seriesId);

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
        Task<IReadOnlyDictionary<Guid, (int Total, int Local)>> GetEpisodeCountsForSeriesAsync(IReadOnlyList<Guid> seriesIds);

        /// <summary>
        /// Liefert alle Episoden mehrerer Serien in einer einzigen Datenbankabfrage.
        /// Ersetzt den N+1-Zugriff: statt einer Abfrage pro Serie wird ein einziger Query
        /// mit <c>WHERE SeriesId IN (...)</c> ausgeführt.
        /// </summary>
        /// <param name="seriesIds">IDs der Serien, deren Episoden geladen werden sollen.</param>
        /// <returns>Alle Episoden der angegebenen Serien, unsortiert.</returns>
        Task<IReadOnlyList<Episode>> GetBySeriesIdsAsync(IReadOnlyList<Guid> seriesIds);

        /// <summary>
        /// Liefert eine Episode anhand ihrer eindeutigen ID.
        /// </summary>
        Task<Episode?> GetByIdAsync(Guid id);

        /// <summary>
        /// Fügt eine neue Episode dauerhaft hinzu.
        /// </summary>
        Task AddAsync(Episode episode);

        /// <summary>
        /// Aktualisiert eine bestehende Episode.
        /// </summary>
        Task UpdateAsync(Episode episode);

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
        Task<IReadOnlyList<Episode>> GetMissingLocalEpisodesAsync(Guid seriesId);

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
        Task<int?> GetHighestLocalEpisodeNumberAsync(Guid seriesId);

        /// <summary>
        /// Setzt das lokal gespeicherte Cover einer Episode.
        /// Überschreibt immer den vorhandenen Wert – ein manuell gesetztes Cover hat
        /// Vorrang vor automatisch ermittelten Daten.
        /// Existiert die Episode nicht, wird der Aufruf ignoriert.
        /// </summary>
        /// <param name="episodeId">Die ID der Episode.</param>
        /// <param name="coverData">
        /// Rohe Bilddaten als Byte-Array.
        /// <see langword="null"/> entfernt das gespeicherte Cover.
        /// </param>
        Task SetLocalCoverAsync(Guid episodeId, byte[]? coverData);

        /// <summary>
        /// Setzt den Zeitstempel der letzten Cover-Suche.
        /// Wird nach jeder automatischen Suche gesetzt, unabhängig vom Ergebnis.
        /// Verhindert wiederholtes Durchsuchen bei Episoden ohne Treffer (Cooldown).
        /// </summary>
        /// <param name="episodeId">Die ID der Episode.</param>
        /// <param name="checkedAt">Zeitpunkt der Prüfung (UTC).</param>
        Task SetCoverLastCheckedAsync(Guid episodeId, DateTime checkedAt);

        /// <summary>
        /// Markiert eine Episode als gelöscht (Soft-Delete).
        /// </summary>
        Task DeleteAsync(Guid id);
    }
}