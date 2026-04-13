using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Internal;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EchoPlay.Data.Services
{
    /// <summary>
    /// EF-Core-basierte Implementierung des Datenservices für Serien.
    /// Diese Klasse kapselt den technischen Datenzugriff und stellt sicher, dass alle Zugriffe und Änderungen den definierten Soft-Delete- und
    /// Cascade-Regeln des Projekts entsprechen.
    /// </summary>
    /// <remarks>Initialisiert eine neue Instanz des <see cref="SeriesDataService"/>.</remarks>
    /// <param name="context">Der zu verwendende EF-Core-Datenbankkontext.</param>
    /// <param name="loggerFactory">Die Logger-Factory zur Erstellung des Loggers.</param>
    public sealed class SeriesDataService(
        EchoPlayDbContext context,
        EchoPlay.Logger.Abstractions.ILoggerFactory loggerFactory) : ISeriesDataService
    {
        private readonly EchoPlayDbContext _context = context;
        private readonly EchoPlay.Logger.Abstractions.ILogger _logger = loggerFactory.CreateLogger("SeriesDataService");

        /// <summary>
        /// Liefert alle nicht logisch gelöschten Serien sortiert nach Titel.
        /// Der globale Soft-Delete-QueryFilter stellt sicher, dass gelöschte Datensätze standardmäßig nicht berücksichtigt werden.
        /// </summary>
        public async Task<IReadOnlyList<Series>> GetAllAsync()
        {
            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope("DB:Series:GetAll");

            _logger.Debug("Lade alle Serien.");

            List<Series> result = await _context.Series

                .OrderBy(series => series.Title)
                .ToListAsync().ConfigureAwait(false);

            _logger.Debug($"{result.Count} Serien geladen.");

            return result;
        }

        /// <summary>
        /// Liefert eine Serie anhand ihrer eindeutigen ID oder <c>null</c>,
        /// wenn sie nicht existiert oder logisch gelöscht wurde.
        /// Der Zugriff erfolgt über <see cref="DbSet{TEntity}.FindAsync(object[])"/>, da es sich um einen Primary-Key-Zugriff handelt und diese Methode
        /// optimal für Skalierung und Change-Tracker-Nutzung geeignet ist.
        /// </summary>
        /// <param name="id">Die eindeutige Serien-ID.</param>
        public async Task<Series?> GetByIdAsync(Guid id)
        {
            _logger.Debug($"Lade Serie mit ID '{id}'.");
            return await _context.Series.FindAsync(id).ConfigureAwait(false);
        }

        /// <summary>
        /// Liefert eine Serie anhand der Spotify-Artist-ID oder <c>null</c>, wenn kein entsprechender Eintrag vorhanden ist.
        /// Da es sich hierbei um einen fachlichen Suchschlüssel handelt,
        /// wird FirstOrDefaultAsync verwendet.
        /// </summary>
        /// <param name="spotifyArtistId">Die Spotify-Artist-ID.</param>
        public async Task<Series?> GetBySpotifyArtistIdAsync(string spotifyArtistId)
        {
            _logger.Debug($"Lade Serie mit Spotify-Artist-ID '{spotifyArtistId}'.");
            return await _context.Series
                .FirstOrDefaultAsync(series => series.SpotifyArtistId == spotifyArtistId).ConfigureAwait(false);
        }

        /// <summary>
        /// Liefert eine Serie anhand der Apple-Music-Artist-ID oder <c>null</c>, wenn kein entsprechender Eintrag vorhanden ist.
        /// </summary>
        /// <param name="appleMusicArtistId">Die Apple-Music-Artist-ID.</param>
        public async Task<Series?> GetByAppleMusicArtistIdAsync(string appleMusicArtistId)
        {
            _logger.Debug($"Lade Serie mit Apple-Music-Artist-ID '{appleMusicArtistId}'.");
            return await _context.Series
                .FirstOrDefaultAsync(series => series.AppleMusicArtistId == appleMusicArtistId).ConfigureAwait(false);
        }

        /// <summary>
        /// Fügt eine neue Serie dauerhaft hinzu.
        /// </summary>
        /// <param name="series">Die zu persistierende Serie.</param>
        public async Task AddAsync(Series series)
        {
            ArgumentNullException.ThrowIfNull(series);

            _ = _context.Series.Add(series);
            _ = await _context.SaveChangesAsync().ConfigureAwait(false);
            _logger.Info($"Serie '{series.Title}' (ID: {series.Id}) hinzugefügt.");
        }

        /// <summary>
        /// Aktualisiert eine bestehende Serie.
        /// Es wird davon ausgegangen, dass die Entität bereits aus dem aktuellen DbContext stammt.
        /// </summary>
        /// <param name="series">Die zu aktualisierende Serie.</param>
        public async Task UpdateAsync(Series series)
        {
            ArgumentNullException.ThrowIfNull(series);

            _ = _context.Series.Update(series);
            _ = await _context.SaveChangesAsync().ConfigureAwait(false);
            _logger.Info($"Serie '{series.Title}' (ID: {series.Id}) aktualisiert.");
        }

        /// <summary>
        /// Liefert alle abonnierten, nicht gelöschten Serien, sortiert nach Titel.
        /// Der globale QueryFilter schließt bereits gelöschte Serien aus.
        /// </summary>
        public async Task<IReadOnlyList<Series>> GetSubscribedAsync()
        {
            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope("DB:Series:GetSubscribed");

            _logger.Debug("Lade abonnierte Serien.");

            List<Series> result = await _context.Series

                .Where(series => series.IsSubscribed)
                .OrderBy(series => series.Title)
                .ToListAsync().ConfigureAwait(false);

            _logger.Debug($"{result.Count} abonnierte Serie(n) geladen.");

            return result;
        }

        /// <summary>
        /// Setzt den Abonnementstatus einer Serie dauerhaft.
        /// Existiert die Serie nicht, wird der Aufruf mit einer Warnung ignoriert.
        /// </summary>
        /// <param name="seriesId">Die ID der Serie.</param>
        /// <param name="isSubscribed"><see langword="true"/> zum Abonnieren, <see langword="false"/> zum Abbestellen.</param>
        public async Task SetSubscribedAsync(Guid seriesId, bool isSubscribed)
        {
            Series? series = await _context.Series
                .AsTracking()
                .FirstOrDefaultAsync(s => s.Id == seriesId).ConfigureAwait(false);

            if (series is null)
            {
                _logger.Warning($"Serie '{seriesId}' nicht gefunden – IsSubscribed-Update übersprungen.");
                return;
            }

            series.IsSubscribed = isSubscribed;
            _ = await _context.SaveChangesAsync().ConfigureAwait(false);

            _logger.Info($"Serie '{series.Title}' (ID: {seriesId}) IsSubscribed = {isSubscribed}.");
        }

        /// <summary>
        /// Liefert alle favorisierten, nicht gelöschten Serien, sortiert nach Titel.
        /// Der globale QueryFilter schließt bereits gelöschte Serien aus.
        /// </summary>
        public async Task<IReadOnlyList<Series>> GetFavoritesAsync()
        {
            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope("DB:Series:GetFavorites");

            _logger.Debug("Lade favorisierte Serien.");

            List<Series> result = await _context.Series

                .Where(series => series.IsFavorite)
                .OrderBy(series => series.Title)
                .ToListAsync().ConfigureAwait(false);

            _logger.Debug($"{result.Count} favorisierte Serie(n) geladen.");

            return result;
        }

        /// <summary>
        /// Setzt den Favoritenstatus einer Serie dauerhaft.
        /// Existiert die Serie nicht, wird der Aufruf mit einer Warnung ignoriert.
        /// </summary>
        /// <param name="seriesId">Die ID der Serie.</param>
        /// <param name="isFavorite"><see langword="true"/> zum Favorisieren, <see langword="false"/> zum Entfernen.</param>
        public async Task SetFavoriteAsync(Guid seriesId, bool isFavorite)
        {
            Series? series = await _context.Series
                .AsTracking()
                .FirstOrDefaultAsync(s => s.Id == seriesId).ConfigureAwait(false);

            if (series is null)
            {
                _logger.Warning($"Serie '{seriesId}' nicht gefunden – IsFavorite-Update übersprungen.");
                return;
            }

            series.IsFavorite = isFavorite;
            _ = await _context.SaveChangesAsync().ConfigureAwait(false);

            _logger.Info($"Serie '{series.Title}' (ID: {seriesId}) IsFavorite = {isFavorite}.");
        }

        /// <summary>
        /// Setzt das lokal gespeicherte Cover einer Serie dauerhaft.
        /// Existiert die Serie nicht, wird der Aufruf mit einer Warnung ignoriert.
        /// </summary>
        /// <param name="seriesId">Die ID der Serie.</param>
        /// <param name="coverData">Rohe Bilddaten oder <see langword="null"/> zum Entfernen.</param>
        public async Task SetLocalCoverAsync(Guid seriesId, byte[]? coverData)
        {
            Series? series = await _context.Series
                .AsTracking()
                .FirstOrDefaultAsync(s => s.Id == seriesId).ConfigureAwait(false);

            if (series is null)
            {
                _logger.Warning($"Serie '{seriesId}' nicht gefunden – Cover-Update übersprungen.");
                return;
            }

            series.LocalCoverData = coverData;
            _ = await _context.SaveChangesAsync().ConfigureAwait(false);

            _logger.Info($"Cover für Serie '{series.Title}' (ID: {seriesId}) {(coverData is null ? "gelöscht" : "gespeichert")}.");
        }

        /// <inheritdoc/>
        public async Task SetWatchedAsync(Guid seriesId, bool isWatched)
        {
            Series? series = await _context.Series
                .AsTracking()
                .FirstOrDefaultAsync(s => s.Id == seriesId).ConfigureAwait(false);

            if (series is null)
            {
                _logger.Warning($"Serie '{seriesId}' nicht gefunden – IsWatched-Update übersprungen.");
                return;
            }

            series.IsWatched = isWatched;
            _ = await _context.SaveChangesAsync().ConfigureAwait(false);

            _logger.Info($"Serie \"{series.Title}\" – Überwachung {(isWatched ? "aktiviert" : "deaktiviert")}.");
        }

        /// <summary>
        /// Markiert eine Serie sowie alle untergeordneten Episoden und deren PlaybackStates als logisch gelöscht.
        /// Diese Methode setzt die oberste Regel der Soft-Delete-Matrix um:
        /// Eine gelöschte Serie darf keine aktiven oder sichtbaren Kindelemente besitzen.
        /// </summary>
        /// <param name="id">Die eindeutige ID der zu löschenden Serie.</param>
        public async Task DeleteAsync(Guid id)
        {
            using EchoPlay.Logger.Scoping.LogScope scope = _logger.BeginScope($"DB:Series:Delete:{id}");

            Series? series = await _context.Series
                .AsTracking()
                .FirstOrDefaultAsync(s => s.Id == id).ConfigureAwait(false);

            if (series == null)
            {
                // Wenn die Serie nicht existiert, ist kein Soft-Delete erforderlich.
                _logger.Warning($"Serie mit ID '{id}' nicht gefunden – Soft-Delete übersprungen.");
                return;
            }

            // Transaktion stellt sicher, dass Serie, Episoden und PlaybackStates
            // atomar als gelöscht markiert werden. Ein Fehler während SaveChanges
            // rollback alle Änderungen vollständig.
            IDbContextTransaction transaction =
                await _context.Database.BeginTransactionAsync().ConfigureAwait(false);

            try
            {
                series.MarkAsDeleted(EntityClock.Current.UtcNow);

                List<Episode> episodes =
                    await _context.Episodes
                        .AsTracking()
                        .Where(episode => episode.SeriesId == id)
                        .ToListAsync().ConfigureAwait(false);

                // Alle EpisodeIds sammeln, um PlaybackStates in einem einzigen Query zu laden statt pro Episode einzeln (N+1 vermeiden).
                List<Guid> episodeIds = episodes.Select(episode => episode.Id).ToList();

                List<PlaybackState> playbackStates =
                    await _context.PlaybackStates
                        .AsTracking()
                        .Where(state => episodeIds.Contains(state.EpisodeId))
                        .ToListAsync().ConfigureAwait(false);

                foreach (Episode episode in episodes)
                {
                    episode.MarkAsDeleted(EntityClock.Current.UtcNow);
                }

                foreach (PlaybackState playbackState in playbackStates)
                {
                    playbackState.MarkAsDeleted(EntityClock.Current.UtcNow);
                }

                _ = await _context.SaveChangesAsync().ConfigureAwait(false);
                await transaction.CommitAsync().ConfigureAwait(false);

                _logger.Info($"Serie '{series.Title}' (ID: {id}) und {episodes.Count} Episode(n) sowie {playbackStates.Count} PlaybackState(s) als gelöscht markiert.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync().ConfigureAwait(false);
                _logger.Error($"Soft-Delete für Serie '{id}' fehlgeschlagen – Transaktion zurückgerollt.", ex);
                throw;
            }
            finally
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
