using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
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
        /// <summary>Lädt das Cover einer Serie als BitmapImage; <c>null</c>, wenn keines vorhanden ist.</summary>
        /// <param name="seriesId">Die ID der Serie.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Das Cover oder <c>null</c>.</returns>
        Task<BitmapImage?> GetSeriesCoverImageAsync(Guid seriesId, CancellationToken cancellationToken = default);

        /// <summary>Lädt das Cover einer Episode als BitmapImage; <c>null</c>, wenn keines vorhanden ist.</summary>
        /// <param name="episodeId">Die ID der Episode.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Das Cover oder <c>null</c>.</returns>
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
        /// <returns>Asynchrone Ausführung.</returns>
        [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "sourceUrl wird als string in der DB-Spalte Covers.SourceUrl persistiert.")]
        Task SetSeriesCoverAsync(Guid seriesId, byte[] imageData, string? sourceUrl = null, CancellationToken cancellationToken = default);

        /// <summary>Speichert ein Cover für eine Episode (mit Retry bei DB-Fehlern).</summary>
        /// <param name="episodeId">Die ID der Episode.</param>
        /// <param name="imageData">Die Bilddaten.</param>
        /// <param name="sourceUrl">Optionale Quell-URL.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "sourceUrl wird als string in der DB-Spalte Covers.SourceUrl persistiert.")]
        Task SetEpisodeCoverAsync(Guid episodeId, byte[] imageData, string? sourceUrl = null, CancellationToken cancellationToken = default);

        /// <summary>Prüft, ob ein Cover für eine Serie existiert (ohne den Blob zu laden).</summary>
        /// <param name="seriesId">Die ID der Serie.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns><c>true</c>, wenn ein Cover existiert.</returns>
        Task<bool> HasSeriesCoverAsync(Guid seriesId, CancellationToken cancellationToken = default);
    }
}
