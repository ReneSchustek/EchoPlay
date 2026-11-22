using Microsoft.UI.Xaml.Media.Imaging;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Zentrale Anlaufstelle für alle Cover-Operationen in der App-Schicht: Laden als
    /// <see cref="BitmapImage"/> oder Binärdaten (Batch), Speichern und Existenzprüfung.
    /// Kapselt den Zugriff auf die CoverImages-Tabelle vor den ViewModels.
    /// </summary>
    public interface ICoverService
    {
        /// <summary>Lädt das Cover einer Serie als BitmapImage; <see langword="null"/>, wenn keines vorhanden ist.</summary>
        /// <param name="seriesId">Die ID der Serie.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Das Cover oder <see langword="null"/>.</returns>
        Task<BitmapImage?> GetSeriesCoverImageAsync(Guid seriesId, CancellationToken cancellationToken = default);

        /// <summary>Lädt das Cover einer Episode als BitmapImage; <see langword="null"/>, wenn keines vorhanden ist.</summary>
        /// <param name="episodeId">Die ID der Episode.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Das Cover oder <see langword="null"/>.</returns>
        Task<BitmapImage?> GetEpisodeCoverImageAsync(Guid episodeId, CancellationToken cancellationToken = default);

        /// <summary>Lädt Cover-Binärdaten für mehrere Episoden in einer Query (Batch, kein N+1).</summary>
        /// <param name="episodeIds">Die IDs der Episoden.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Zuordnung Episode-ID → Cover-Bytes für vorhandene Cover.</returns>
        Task<IReadOnlyDictionary<Guid, byte[]>> GetEpisodeCoverBytesAsync(IReadOnlyList<Guid> episodeIds, CancellationToken cancellationToken = default);

        /// <summary>Speichert ein Cover für eine Serie (mit Retry bei DB-Fehlern).</summary>
        /// <param name="seriesId">Die ID der Serie.</param>
        /// <param name="imageData">Die Bilddaten.</param>
        /// <param name="sourceUrl">Optionale Quell-URL.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Der Task ist abgeschlossen, wenn das Cover in der Datenbank steht oder alle Wiederholungen gescheitert sind.</returns>
        Task SetSeriesCoverAsync(Guid seriesId, byte[] imageData, Uri? sourceUrl = null, CancellationToken cancellationToken = default);

        /// <summary>Speichert ein Cover für eine Episode (mit Retry bei DB-Fehlern).</summary>
        /// <param name="episodeId">Die ID der Episode.</param>
        /// <param name="imageData">Die Bilddaten.</param>
        /// <param name="sourceUrl">Optionale Quell-URL.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Der Task ist abgeschlossen, wenn das Cover in der Datenbank steht oder alle Wiederholungen gescheitert sind.</returns>
        Task SetEpisodeCoverAsync(Guid episodeId, byte[] imageData, Uri? sourceUrl = null, CancellationToken cancellationToken = default);

        /// <summary>Prüft, ob ein Cover für eine Serie existiert (ohne den Blob zu laden).</summary>
        /// <param name="seriesId">Die ID der Serie.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns><see langword="true"/>, wenn ein Cover existiert.</returns>
        Task<bool> HasSeriesCoverAsync(Guid seriesId, CancellationToken cancellationToken = default);
    }
}
