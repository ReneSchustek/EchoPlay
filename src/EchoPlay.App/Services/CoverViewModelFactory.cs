using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EchoPlay.Core;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Bauplan für eine ViewModel-Cover-Quelle.
    /// Zentrale Stelle für die Fallback-Kaskade DB-Cover → cover.jpg → URL/ID3 → null.
    /// Verhindert dass dieselbe Logik in <c>DashboardDataLoader</c> und
    /// <c>SeriesDetailViewModel</c> auseinanderdriftet.
    /// </summary>

    public interface ICoverViewModelFactory
    {
        /// <summary>
        /// Liefert ein Cover für die Serie. Priorität: DB-Cover → cover.jpg im Serienordner → URL → null.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <param name="series">Parameter <c>series</c>.</param>
        Task<BitmapImage?> BuildSeriesCoverAsync(Series? series, CancellationToken cancellationToken = default);

        /// <summary>
        /// Liefert ein Cover für die Episode. Priorität: DB-Cover → cover.jpg im Ordner → ID3-Tag → null.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <param name="episode">Parameter <c>episode</c>.</param>
        Task<BitmapImage?> BuildEpisodeCoverAsync(Episode episode, CancellationToken cancellationToken = default);
    }

    /// <inheritdoc/>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Cover-Build darf einzelne IO-/Dekodier-Fehler nicht zum Abbruch führen — 'null' führt zum Fallback-Cover oder Platzhalter.")]
    public sealed class CoverViewModelFactory : ICoverViewModelFactory
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly EchoPlay.App.Services.ICoverService? _coverService;

        /// <summary>
        /// Initialisiert die Factory. <paramref name="coverService"/> ist optional —
        /// in Test-Konstellationen ohne Cover-Service liefert die Factory `null` für
        /// alle DB-Cover-Pfade und fällt sofort auf den Datei-/URL-Pfad zurück.
        /// </summary>


        /// <param name="scopeFactory">Parameter <c>scopeFactory</c>.</param>
        /// <param name="coverService">Parameter <c>coverService</c>.</param>
        public CoverViewModelFactory(IServiceScopeFactory scopeFactory, ICoverService? coverService = null)
        {
            ArgumentNullException.ThrowIfNull(scopeFactory);
            _scopeFactory = scopeFactory;
            _coverService = coverService;
        }

        /// <inheritdoc/>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        /// <param name="series">Parameter <c>series</c>.</param>
        public async Task<BitmapImage?> BuildSeriesCoverAsync(Series? series, CancellationToken cancellationToken = default)
        {
            if (series is null)
            {
                return null;
            }

            if (_coverService is not null)
            {
                BitmapImage? dbCover = await _coverService.GetSeriesCoverImageAsync(series.Id, cancellationToken).ConfigureAwait(true);
                if (dbCover is not null)
                {
                    return dbCover;
                }
            }

            if (series.LocalFolderPath is not null)
            {
                string coverPath = Path.Combine(series.LocalFolderPath, CoverConstants.CoverFileName);
                if (File.Exists(coverPath))
                {
                    try
                    {
                        byte[] bytes = await File.ReadAllBytesAsync(coverPath, cancellationToken).ConfigureAwait(true);
                        return await CoverService.ConvertToBitmapAsync(bytes, cancellationToken).ConfigureAwait(true);
                    }
                    catch
                    {
                        // Datei-Zugriffsfehler still ignorieren — Fallback auf URL/null.
                    }
                }
            }

            if (series.CoverImageUrl is not null)
            {
                return new BitmapImage(new Uri(series.CoverImageUrl));
            }

            return null;
        }

        /// <inheritdoc/>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        /// <param name="episode">Parameter <c>episode</c>.</param>
        public async Task<BitmapImage?> BuildEpisodeCoverAsync(Episode episode, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(episode);

            if (_coverService is not null)
            {
                BitmapImage? dbCover = await _coverService.GetEpisodeCoverImageAsync(episode.Id, cancellationToken).ConfigureAwait(true);
                if (dbCover is not null)
                {
                    return dbCover;
                }
            }

            if (episode.LocalFolderPath is not null)
            {
                try
                {
                    using IServiceScope scope = _scopeFactory.CreateScope();
                    ILocalCoverLoader coverLoader = scope.ServiceProvider.GetRequiredService<ILocalCoverLoader>();

                    // ID3-Fallback nur, wenn kein cover.jpg vorhanden — spart DB-Abfrage.
                    string? firstTrackPath = null;
                    if (!File.Exists(Path.Combine(episode.LocalFolderPath, CoverConstants.CoverFileName)))
                    {
                        ILocalTrackDataService trackService = scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();
                        IReadOnlyList<LocalTrack> tracks = await trackService.GetByEpisodeIdAsync(episode.Id, cancellationToken).ConfigureAwait(true);
                        firstTrackPath = tracks.OrderBy(t => t.TrackNumber).FirstOrDefault()?.FilePath;
                    }

                    byte[]? coverBytes = await coverLoader.LoadAsync(episode.LocalFolderPath, firstTrackPath).ConfigureAwait(true);
                    if (coverBytes is not null)
                    {
                        return await CoverService.ConvertToBitmapAsync(coverBytes, cancellationToken).ConfigureAwait(true);
                    }
                }
                catch
                {
                    // IO-/Bild-Dekodier-Fehler einer einzelnen Episode dürfen die Ansicht nicht blockieren.
                }
            }

            return null;
        }
    }
}
