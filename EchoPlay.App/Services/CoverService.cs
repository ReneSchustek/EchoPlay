using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Zentrale Anlaufstelle für alle Cover-Operationen in der App-Schicht.
    /// Kapselt den Zugriff auf die CoverImages-Tabelle und die Konvertierung
    /// von Binärdaten zu BitmapImage. Kein ViewModel greift direkt auf
    /// den ICoverImageDataService zu.
    /// </summary>

    public sealed class CoverService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;

        /// <summary>Entity-Typ für Serien-Cover in der CoverImages-Tabelle.</summary>

        public const string EntityTypeSeries = CoverEntityTypes.Series;

        /// <summary>Entity-Typ für Episoden-Cover in der CoverImages-Tabelle.</summary>

        public const string EntityTypeEpisode = CoverEntityTypes.Episode;

        /// <summary>
        /// Initialisiert den CoverService.
        /// </summary>


        /// <param name="scopeFactory">Parameter <c>scopeFactory</c>.</param>
        /// <param name="loggerFactory">Parameter <c>loggerFactory</c>.</param>
        public CoverService(IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory;
            _logger = loggerFactory.CreateLogger("CoverService");
        }

        /// <summary>
        /// Lädt das Cover einer Serie als BitmapImage.
        /// Null wenn kein Cover vorhanden.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        /// <param name="seriesId">Parameter <c>seriesId</c>.</param>
        public async Task<BitmapImage?> GetSeriesCoverImageAsync(Guid seriesId, CancellationToken cancellationToken = default)
        {
            byte[]? bytes = await GetCoverBytesAsync(EntityTypeSeries, seriesId, cancellationToken);
            return bytes is not null ? await ConvertToBitmapAsync(bytes, cancellationToken) : null;
        }

        /// <summary>
        /// Lädt das Cover einer Episode als BitmapImage.
        /// Null wenn kein Cover vorhanden.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        /// <param name="episodeId">Parameter <c>episodeId</c>.</param>
        public async Task<BitmapImage?> GetEpisodeCoverImageAsync(Guid episodeId, CancellationToken cancellationToken = default)
        {
            byte[]? bytes = await GetCoverBytesAsync(EntityTypeEpisode, episodeId, cancellationToken);
            return bytes is not null ? await ConvertToBitmapAsync(bytes, cancellationToken) : null;
        }

        /// <summary>
        /// Lädt Cover-Binärdaten für mehrere Serien in einer Query (Batch).
        /// Verhindert N+1-Probleme beim Laden von Serienlisten.
        /// </summary>

        public async Task<IReadOnlyDictionary<Guid, byte[]>> GetSeriesCoverBytesAsync(
            IReadOnlyList<Guid> seriesIds, CancellationToken cancellationToken = default)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ICoverImageDataService coverService = scope.ServiceProvider
                .GetRequiredService<ICoverImageDataService>();
            return await coverService.GetImageDataByEntitiesAsync(EntityTypeSeries, seriesIds, cancellationToken);
        }

        /// <summary>
        /// Lädt Cover-Binärdaten für mehrere Episoden in einer Query (Batch).
        /// Verhindert N+1-Probleme beim Laden von Episodenlisten.
        /// </summary>

        public async Task<IReadOnlyDictionary<Guid, byte[]>> GetEpisodeCoverBytesAsync(
            IReadOnlyList<Guid> episodeIds, CancellationToken cancellationToken = default)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ICoverImageDataService coverService = scope.ServiceProvider
                .GetRequiredService<ICoverImageDataService>();
            return await coverService.GetImageDataByEntitiesAsync(EntityTypeEpisode, episodeIds, cancellationToken);
        }

        /// <summary>
        /// Speichert ein Cover für eine Serie mit 3-Retry bei DB-Fehlern.
        /// </summary>
        [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
            Justification = "sourceUrl wird als string in der DB-Spalte Covers.SourceUrl persistiert.")]
        public async Task SetSeriesCoverAsync(Guid seriesId, byte[] imageData, string? sourceUrl = null, CancellationToken cancellationToken = default)
        {
            await WriteWithRetryAsync(EntityTypeSeries, seriesId, imageData, sourceUrl, cancellationToken);
        }

        /// <summary>
        /// Speichert ein Cover für eine Episode mit 3-Retry bei DB-Fehlern.
        /// </summary>
        [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
            Justification = "sourceUrl wird als string in der DB-Spalte Covers.SourceUrl persistiert.")]
        public async Task SetEpisodeCoverAsync(Guid episodeId, byte[] imageData, string? sourceUrl = null, CancellationToken cancellationToken = default)
        {
            await WriteWithRetryAsync(EntityTypeEpisode, episodeId, imageData, sourceUrl, cancellationToken);
        }

        /// <summary>
        /// Prüft ob ein Cover für eine Serie existiert (ohne Blob zu laden).
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        /// <param name="seriesId">Parameter <c>seriesId</c>.</param>
        public async Task<bool> HasSeriesCoverAsync(Guid seriesId, CancellationToken cancellationToken = default)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ICoverImageDataService coverService = scope.ServiceProvider
                .GetRequiredService<ICoverImageDataService>();
            return await coverService.ExistsAsync(EntityTypeSeries, seriesId, cancellationToken);
        }

        /// <summary>
        /// Konvertiert Binärdaten in ein BitmapImage für die UI.
        /// Null bei fehlerhaften Daten (korruptes Bild).
        /// </summary>
        /// <param name="imageData">Rohe Bild-Bytes (JPEG/PNG/...).</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Bild-Dekodierung via BitmapImage.SetSourceAsync: kaputte oder zugeschnittene Cover-Rohdaten liefern native WIC/COM-Fehler; für die UI reicht 'null' (Fallback-Cover).")]
        public static async Task<BitmapImage?> ConvertToBitmapAsync(byte[] imageData, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                BitmapImage bitmap = new();
                using InMemoryRandomAccessStream stream = new();
                _ = await stream.WriteAsync(imageData.AsBuffer());
                stream.Seek(0);
                await bitmap.SetSourceAsync(stream);
                return bitmap;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Schreibt Cover-Daten mit bis zu 3 Versuchen und exponentiellem Backoff.
        /// Jeder Versuch öffnet einen frischen DI-Scope, damit ein korrupter DbContext-Zustand
        /// den nächsten Versuch nicht blockiert.
        /// </summary>



        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        /// <param name="entityType">Parameter <c>entityType</c>.</param>
        /// <param name="entityId">Parameter <c>entityId</c>.</param>
        /// <param name="imageData">Parameter <c>imageData</c>.</param>
        /// <param name="sourceUrl">Parameter <c>sourceUrl</c>.</param>
        private async Task WriteWithRetryAsync(string entityType, Guid entityId, byte[] imageData, string? sourceUrl, CancellationToken cancellationToken = default)
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using IServiceScope scope = _scopeFactory.CreateScope();
                    ICoverImageDataService coverService = scope.ServiceProvider
                        .GetRequiredService<ICoverImageDataService>();
                    await coverService.SetCoverAsync(entityType, entityId, imageData, sourceUrl, cancellationToken);
                    return;
                }
                catch (DbUpdateException ex) when (attempt < 3)
                {
                    _logger.Warning($"Cover-DB-Write Retry {attempt}/3 für {entityType} {entityId}: {ex.Message}");
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Lädt Cover-Binärdaten für eine einzelne Entity.
        /// </summary>

        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        /// <param name="entityType">Parameter <c>entityType</c>.</param>
        /// <param name="entityId">Parameter <c>entityId</c>.</param>
        private async Task<byte[]?> GetCoverBytesAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ICoverImageDataService coverService = scope.ServiceProvider
                .GetRequiredService<ICoverImageDataService>();

            Data.Entities.Library.CoverImage? cover =
                await coverService.GetByEntityAsync(entityType, entityId, cancellationToken);

            return cover?.ImageData is { Length: > 0 } ? cover.ImageData : null;
        }
    }
}
