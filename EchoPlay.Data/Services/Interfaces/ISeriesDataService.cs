using EchoPlay.Data.Entities.Library;

namespace EchoPlay.Data.Services.Interfaces
{
    /// <summary>
    /// Definiert den stabilen Vertrag für den Zugriff auf persistierte Hörspielserien.
    /// Gewährleistet eine technologische Abstraktion vom zugrunde liegenden Speichersystem.
    /// </summary>
    public interface ISeriesDataService
    {
        /// <summary>
        /// Liefert alle aktiven Hörspielserien, sortiert nach Titel.
        /// </summary>
        /// <returns>Eine schreibgeschützte Liste aller <see cref="Series"/>-Entitäten.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<IReadOnlyList<Series>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Sucht eine Serie anhand ihrer Primärschlüssel-ID.
        /// </summary>
        /// <param name="id">Die eindeutige GUID der Serie.</param>
        /// <returns>Die Serie oder null, falls keine Übereinstimmung gefunden wurde.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<Series?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Findet eine Serie über die externe Spotify-Artist-ID.
        /// </summary>
        /// <param name="spotifyArtistId">Die ID des Künstlers von Spotify.</param>
        /// <returns>Die zugeordnete <see cref="Series"/> oder null.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<Series?> GetBySpotifyArtistIdAsync(string spotifyArtistId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Findet eine Serie über die externe Apple-Music-Artist-ID.
        /// </summary>
        /// <param name="appleMusicArtistId">Die ID des Künstlers von Apple Music.</param>
        /// <returns>Die zugeordnete <see cref="Series"/> oder null.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<Series?> GetByAppleMusicArtistIdAsync(string appleMusicArtistId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Persistiert eine neue Hörspielserie im System.
        /// </summary>
        /// <param name="series">Die zu speichernde Entität.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task AddAsync(Series series, CancellationToken cancellationToken = default);

        /// <summary>
        /// Aktualisiert die Daten einer bestehenden Serie.
        /// </summary>
        /// <param name="series">Die Entität mit aktualisierten Werten.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task UpdateAsync(Series series, CancellationToken cancellationToken = default);

        /// <summary>
        /// Liefert alle abonnierten, nicht logisch gelöschten Serien, sortiert nach Titel.
        /// </summary>
        /// <returns>Eine schreibgeschützte Liste aller abonnierten <see cref="Series"/>-Entitäten.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<IReadOnlyList<Series>> GetSubscribedAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Setzt den Abonnementstatus einer Serie.
        /// Hat keinen Einfluss auf Episoden oder PlaybackStates.
        /// Existiert die Serie nicht, wird der Aufruf ignoriert.
        /// </summary>
        /// <param name="seriesId">Die ID der Serie.</param>
        /// <param name="isSubscribed"><see langword="true"/> zum Abonnieren, <see langword="false"/> zum Abbestellen.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task SetSubscribedAsync(Guid seriesId, bool isSubscribed, CancellationToken cancellationToken = default);

        /// <summary>
        /// Liefert alle favorisierten, nicht logisch gelöschten Serien, sortiert nach Titel.
        /// </summary>
        /// <returns>Eine schreibgeschützte Liste aller <see cref="Series"/>-Entitäten mit <c>IsFavorite = true</c>.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<IReadOnlyList<Series>> GetFavoritesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Setzt den Favoritenstatus einer Serie.
        /// Hat keinen Einfluss auf Episoden oder PlaybackStates.
        /// Existiert die Serie nicht, wird der Aufruf ignoriert.
        /// </summary>
        /// <param name="seriesId">Die ID der Serie.</param>
        /// <param name="isFavorite"><see langword="true"/> zum Favorisieren, <see langword="false"/> zum Entfernen aus Favoriten.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task SetFavoriteAsync(Guid seriesId, bool isFavorite, CancellationToken cancellationToken = default);

        /// <summary>
        /// Setzt den Überwachungsstatus einer Serie.
        /// Nur überwachte Serien erscheinen im Dashboard unter Neuerscheinungen.
        /// </summary>
        /// <param name="seriesId">Die ID der Serie.</param>
        /// <param name="isWatched">True = Serie wird überwacht, False = nicht überwacht.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task SetWatchedAsync(Guid seriesId, bool isWatched, CancellationToken cancellationToken = default);

        /// <summary>
        /// Führt eine logische Löschung (Soft-Delete) der Serie durch.
        /// </summary>
        /// <param name="id">Die ID der zu löschenden Serie.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    }
}
