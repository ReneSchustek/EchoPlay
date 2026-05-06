using EchoPlay.Data.Entities.Library;

namespace EchoPlay.Data.Services.Interfaces
{
    /// <summary>
    /// Schnittstelle für den Zugriff auf gecachte Neuerscheinungen aus der iTunes-API.
    /// Der Cache ermöglicht es dem Dashboard, gespeicherte Ergebnisse sofort anzuzeigen,
    /// während im Hintergrund ein inkrementelles Update gegen die API läuft.
    /// </summary>
    public interface ICachedNewReleaseDataService
    {
        /// <summary>
        /// Liefert alle gecachten Neuerscheinungen mit zugehöriger Serie.
        /// Sortiert nach <see cref="CachedNewRelease.ReleaseDate"/> absteigend (neueste zuerst).
        /// </summary>
        /// <returns>Alle nicht-gelöschten Cache-Einträge inkl. Series-Navigation.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<IReadOnlyList<CachedNewRelease>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Liefert alle gecachten Neuerscheinungen einer bestimmten Serie.
        /// </summary>
        /// <param name="seriesId">Die ID der Serie.</param>
        /// <returns>Cache-Einträge der Serie, sortiert nach ReleaseDate absteigend.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<IReadOnlyList<CachedNewRelease>> GetBySeriesIdAsync(Guid seriesId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Liefert den Zeitpunkt der letzten iTunes-Prüfung über alle Einträge hinweg.
        /// Wird verwendet, um zu entscheiden, ob ein Hintergrund-Update nötig ist.
        /// </summary>
        /// <returns>
        /// Der jüngste <see cref="CachedNewRelease.CheckedAtUtc"/>-Wert,
        /// oder <c>null</c> wenn der Cache leer ist.
        /// </returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<DateTime?> GetLatestCheckTimeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Fügt neue Einträge hinzu oder aktualisiert bestehende (Upsert per CollectionId).
        /// Bereits vorhandene Einträge (gleiche CollectionId) werden mit den neuen Werten
        /// überschrieben, damit Titeländerungen und Cover-Updates aus iTunes übernommen werden.
        /// </summary>
        /// <param name="entries">Die einzufügenden oder zu aktualisierenden Einträge.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task UpsertRangeAsync(IReadOnlyList<CachedNewRelease> entries, CancellationToken cancellationToken = default);

        /// <summary>
        /// Entfernt alle Einträge, deren <see cref="CachedNewRelease.ReleaseDate"/>
        /// vor dem angegebenen Cutoff liegt. Bereinigt den Cache von veralteten
        /// Neuerscheinungen, die nicht mehr im konfigurierten Zeitfenster liegen.
        /// </summary>
        /// <param name="cutoff">Ältestes erlaubtes Veröffentlichungsdatum.</param>
        /// <returns>Anzahl der entfernten Einträge.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<int> RemoveOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default);

        /// <summary>
        /// Entfernt alle Cache-Einträge, die zu den angegebenen Serien gehören.
        /// Wird beim App-Start aufgerufen, um den Cache für nicht mehr überwachte Serien zu bereinigen.
        /// </summary>
        /// <param name="seriesIds">Die IDs der Serien, deren Cache-Einträge entfernt werden sollen.</param>
        /// <returns>Anzahl der entfernten Einträge.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<int> RemoveBySeriesIdsAsync(IReadOnlyList<Guid> seriesIds, CancellationToken cancellationToken = default);

        /// <summary>
        /// Leert den gesamten Cache. Wird aufgerufen, wenn sich das Zeitfenster
        /// (<see cref="Data.Entities.Settings.AppSettings.NewReleaseDays"/>) vergrößert hat
        /// und ältere Episoden nachgeladen werden müssen.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task ClearAllAsync(CancellationToken cancellationToken = default);
    }
}
