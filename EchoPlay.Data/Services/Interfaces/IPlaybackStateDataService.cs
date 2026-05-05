using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Projections;

namespace EchoPlay.Data.Services.Interfaces
{
    /// <summary>
    /// Schnittstelle für den Zugriff auf Wiedergabestände. Ermöglicht die Persistierung von Fortschrittsdaten pro Episode.
    /// </summary>
    public interface IPlaybackStateDataService
    {
        /// <summary>
        /// Liefert alle aktiven (nicht gelöschten) Wiedergabestände.
        /// Wird für Statistiken verwendet, z.B. Anzahl gehörter und offener Folgen.
        /// </summary>
        /// <returns>Eine schreibgeschützte Liste aller <see cref="PlaybackState"/>-Entitäten.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<IReadOnlyList<PlaybackState>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Lädt den Fortschritt für eine spezifische Episode.
        /// </summary>
        /// <param name="episodeId">Eindeutige ID der Episode.</param>
        /// <returns>Der Status oder null.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<PlaybackState?> GetByEpisodeIdAsync(Guid episodeId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Erstellt einen neuen Datensatz für den Wiedergabestatus.
        /// </summary>
        /// <param name="playbackState">Das zu speichernde Objekt.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task AddAsync(PlaybackState playbackState, CancellationToken cancellationToken = default);

        /// <summary>
        /// Aktualisiert einen bestehenden Status.
        /// </summary>
        /// <param name="playbackState">Das Objekt mit den neuen Werten.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task UpdateAsync(PlaybackState playbackState, CancellationToken cancellationToken = default);

        /// <summary>
        /// Entfernt einen Status-Eintrag logisch.
        /// </summary>
        /// <param name="id">ID des Eintrags.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Berechnet aggregierte Wiedergabe-Zähler für alle Episoden einer Serie in einer einzigen DB-Abfrage.
        /// Ersetzt das N+1-Muster, bei dem für jede Episode ein separater <c>GetByEpisodeIdAsync</c>-Aufruf
        /// nötig wäre.
        /// </summary>
        /// <param name="seriesId">ID der Serie, deren Episoden ausgewertet werden.</param>
        /// <returns>
        /// Tuple mit der Anzahl abgeschlossener, laufender und nicht gestarteter Episoden.
        /// Gibt (0, 0, 0) zurück, wenn keine Episoden oder kein Wiedergabestatus vorhanden sind.
        /// </returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<(int Finished, int InProgress, int NotStarted)> GetCountsBySeriesIdAsync(Guid seriesId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Liefert die Episode-IDs aller abgeschlossenen Wiedergabestände für die angegebenen Episoden.
        /// Ermöglicht effizientes Filtern nach "gehört"/"ungehört" ohne N+1-Problem.
        /// </summary>
        /// <param name="episodeIds">Die zu prüfenden Episode-IDs.</param>
        /// <returns>
        /// HashSet mit den IDs aller Episoden, deren PlaybackState <c>IsCompleted == true</c> hat.
        /// Episoden ohne PlaybackState oder mit <c>IsCompleted == false</c> fehlen im Set.
        /// </returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<HashSet<Guid>> GetCompletedEpisodeIdsAsync(IReadOnlyList<Guid> episodeIds, CancellationToken cancellationToken = default);

        /// <summary>
        /// Liefert die zuletzt aktiven Wiedergabestände als schmale Projektion für Dashboard-Ansichten.
        /// Server-seitig sortiert (DESC nach LastPlayedAt/UpdatedAt/CreatedAt) und auf <paramref name="maxRows"/>
        /// begrenzt – verhindert das Materialisieren der gesamten Tabelle für „Zuletzt gehört".
        /// </summary>
        /// <param name="maxRows">Maximale Anzahl zurückgegebener Zeilen. Werte ≤ 0 liefern eine leere Liste.</param>
        /// <returns>
        /// Liste der jüngsten aktiven Wiedergabestände (IsCompleted oder LastPosition &gt; 0),
        /// absteigend nach Aktivitätszeitpunkt.
        /// </returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<IReadOnlyList<RecentPlaybackRow>> GetRecentActiveAsync(int maxRows, CancellationToken cancellationToken = default);
    }
}
