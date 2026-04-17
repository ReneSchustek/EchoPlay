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
        Task<IReadOnlyList<Series>> GetAllAsync();

        /// <summary>
        /// Sucht eine Serie anhand ihrer Primärschlüssel-ID.
        /// </summary>
        /// <param name="id">Die eindeutige GUID der Serie.</param>
        /// <returns>Die Serie oder null, falls keine Übereinstimmung gefunden wurde.</returns>
        Task<Series?> GetByIdAsync(Guid id);

        /// <summary>
        /// Findet eine Serie über die externe Spotify-Artist-ID.
        /// </summary>
        /// <param name="spotifyArtistId">Die ID des Künstlers von Spotify.</param>
        /// <returns>Die zugeordnete <see cref="Series"/> oder null.</returns>
        Task<Series?> GetBySpotifyArtistIdAsync(string spotifyArtistId);

        /// <summary>
        /// Findet eine Serie über die externe Apple-Music-Artist-ID.
        /// </summary>
        /// <param name="appleMusicArtistId">Die ID des Künstlers von Apple Music.</param>
        /// <returns>Die zugeordnete <see cref="Series"/> oder null.</returns>
        Task<Series?> GetByAppleMusicArtistIdAsync(string appleMusicArtistId);

        /// <summary>
        /// Persistiert eine neue Hörspielserie im System.
        /// </summary>
        /// <param name="series">Die zu speichernde Entität.</param>
        Task AddAsync(Series series);

        /// <summary>
        /// Aktualisiert die Daten einer bestehenden Serie.
        /// </summary>
        /// <param name="series">Die Entität mit aktualisierten Werten.</param>
        Task UpdateAsync(Series series);

        /// <summary>
        /// Liefert alle abonnierten, nicht logisch gelöschten Serien, sortiert nach Titel.
        /// </summary>
        /// <returns>Eine schreibgeschützte Liste aller abonnierten <see cref="Series"/>-Entitäten.</returns>
        Task<IReadOnlyList<Series>> GetSubscribedAsync();

        /// <summary>
        /// Setzt den Abonnementstatus einer Serie.
        /// Hat keinen Einfluss auf Episoden oder PlaybackStates.
        /// Existiert die Serie nicht, wird der Aufruf ignoriert.
        /// </summary>
        /// <param name="seriesId">Die ID der Serie.</param>
        /// <param name="isSubscribed"><see langword="true"/> zum Abonnieren, <see langword="false"/> zum Abbestellen.</param>
        Task SetSubscribedAsync(Guid seriesId, bool isSubscribed);

        /// <summary>
        /// Liefert alle favorisierten, nicht logisch gelöschten Serien, sortiert nach Titel.
        /// </summary>
        /// <returns>Eine schreibgeschützte Liste aller <see cref="Series"/>-Entitäten mit <c>IsFavorite = true</c>.</returns>
        Task<IReadOnlyList<Series>> GetFavoritesAsync();

        /// <summary>
        /// Setzt den Favoritenstatus einer Serie.
        /// Hat keinen Einfluss auf Episoden oder PlaybackStates.
        /// Existiert die Serie nicht, wird der Aufruf ignoriert.
        /// </summary>
        /// <param name="seriesId">Die ID der Serie.</param>
        /// <param name="isFavorite"><see langword="true"/> zum Favorisieren, <see langword="false"/> zum Entfernen aus Favoriten.</param>
        Task SetFavoriteAsync(Guid seriesId, bool isFavorite);

        /// <summary>
        /// Setzt den Überwachungsstatus einer Serie.
        /// Nur überwachte Serien erscheinen im Dashboard unter Neuerscheinungen.
        /// </summary>
        /// <param name="seriesId">Die ID der Serie.</param>
        /// <param name="isWatched">True = Serie wird überwacht, False = nicht überwacht.</param>
        Task SetWatchedAsync(Guid seriesId, bool isWatched);

        /// <summary>
        /// Führt eine logische Löschung (Soft-Delete) der Serie durch.
        /// </summary>
        /// <param name="id">Die ID der zu löschenden Serie.</param>
        Task DeleteAsync(Guid id);
    }
}
