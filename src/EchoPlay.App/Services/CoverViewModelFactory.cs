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
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Bauplan für eine ViewModel-Cover-Quelle.
    /// Zentrale Stelle für die Fallback-Kaskade DB-Cover → cover.jpg → URL/ID3 → null.
    /// Startseite (<c>DashboardCoverProvider</c>) und Serienansicht (<c>SeriesEpisodeTileBuilder</c>)
    /// nutzen sie beide, damit ihre Reihenfolge nicht auseinanderläuft.
    /// </summary>
    public interface ICoverViewModelFactory
    {
        /// <summary>
        /// Liefert ein Cover für die Serie. Priorität: DB-Cover → cover.jpg im Serienordner → URL → null.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <param name="series">Die Serie, deren Cover aufgebaut wird; <see langword="null"/> liefert <see langword="null"/>.</param>
        Task<BitmapImage?> BuildSeriesCoverAsync(Series? series, CancellationToken cancellationToken = default);

        /// <summary>
        /// Liefert ein Cover für die Episode. Priorität: DB-Cover → cover.jpg im Ordner → ID3-Tag → null.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <param name="episode">Die Episode, deren Cover aufgebaut wird.</param>
        Task<BitmapImage?> BuildEpisodeCoverAsync(Episode episode, CancellationToken cancellationToken = default);
    }

    /// <inheritdoc/>
    public sealed class CoverViewModelFactory : ICoverViewModelFactory
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly EchoPlay.App.Services.ICoverService? _coverService;
        private readonly ILogger? _logger;

        /// <summary>
        /// Initialisiert die Factory. <paramref name="coverService"/> ist optional:
        /// in Test-Konstellationen ohne Cover-Service liefert die Factory <see langword="null"/> für
        /// alle DB-Cover-Pfade und fällt sofort auf den Datei-/URL-Pfad zurück.
        /// </summary>
        /// <param name="scopeFactory">Fabrik für kurzlebige DI-Scopes; jeder Datenbankzugriff läuft in einem eigenen.</param>
        /// <param name="coverService">Optionaler Cover-Dienst; fehlt er, wird der Datenbank-Pfad übersprungen.</param>
        /// <param name="logger">Protokolliert übersprungene Cover-Quellen. Nullable für Tests.</param>
        public CoverViewModelFactory(IServiceScopeFactory scopeFactory, ICoverService? coverService = null, ILogger? logger = null)
        {
            ArgumentNullException.ThrowIfNull(scopeFactory);
            _scopeFactory = scopeFactory;
            _coverService = coverService;
            _logger = logger;
        }

        /// <inheritdoc/>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <param name="series">Die Serie, deren Cover aufgebaut wird; <see langword="null"/> liefert <see langword="null"/>.</param>
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

            if (series.LocalFolderPath is not null
                && await TryLoadSeriesCoverFileAsync(series, series.LocalFolderPath, cancellationToken).ConfigureAwait(true) is { } fileCover)
            {
                return fileCover;
            }

            // Die Spalte stammt aus Anbieter-Antworten und älteren Beständen; was sich nicht als
            // absolute Adresse lesen lässt, führt zum Platzhalter statt zu einer Ausnahme.
            if (Uri.TryCreate(series.CoverImageUrl, UriKind.Absolute, out Uri? coverUri))
            {
                return new BitmapImage(coverUri);
            }

            return null;
        }

        /// <summary>
        /// Liest die <c>cover.jpg</c> im Serienordner; ist sie nicht lesbar, <see langword="null"/>.
        /// </summary>
        private async Task<BitmapImage?> TryLoadSeriesCoverFileAsync(Series series, string folder, CancellationToken cancellationToken)
        {
            string coverPath = Path.Combine(folder, CoverConstants.CoverFileName);
            if (!File.Exists(coverPath))
            {
                return null;
            }

            try
            {
                byte[] bytes = await File.ReadAllBytesAsync(coverPath, cancellationToken).ConfigureAwait(true);
                return await CoverService.ConvertToBitmapAsync(bytes, cancellationToken: cancellationToken).ConfigureAwait(true);
            }
            catch (IOException ex)
            {
                // Gesperrt oder zwischen Prüfung und Lesen verschwunden: weiter mit der Adresse.
                _logger?.Debug(() => $"Cover-Datei der Serie '{series.Title}' nicht lesbar: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger?.Debug(() => $"Cover-Datei der Serie '{series.Title}' nicht lesbar: {ex.Message}");
            }

            return null;
        }

        /// <inheritdoc/>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <param name="episode">Die Episode, deren Cover aufgebaut wird.</param>
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

            return episode.LocalFolderPath is null
                ? null
                : await TryLoadLocalEpisodeCoverAsync(episode, episode.LocalFolderPath, cancellationToken).ConfigureAwait(true);
        }

        /// <summary>
        /// Lädt das Cover aus dem Folgenordner, ersatzweise aus den Tags der ersten Spur.
        /// </summary>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Der lokale Weg liest Audio-Tags über TagLib#, das beschädigte Dateien mit wechselnden, nicht gemeinsam abgeleiteten Ausnahmetypen meldet. Eine unlesbare Folge fällt auf den Platzhalter zurück; ein Abbruch wird durchgereicht.")]
        private async Task<BitmapImage?> TryLoadLocalEpisodeCoverAsync(Episode episode, string folder, CancellationToken cancellationToken)
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                ILocalCoverLoader coverLoader = scope.ServiceProvider.GetRequiredService<ILocalCoverLoader>();

                // ID3-Fallback nur, wenn kein cover.jpg vorhanden ist (spart DB-Abfrage).
                string? firstTrackPath = null;
                if (!File.Exists(Path.Combine(folder, CoverConstants.CoverFileName)))
                {
                    ILocalTrackDataService trackService = scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();
                    IReadOnlyList<LocalTrack> tracks = await trackService.GetByEpisodeIdAsync(episode.Id, cancellationToken).ConfigureAwait(true);
                    firstTrackPath = tracks.OrderBy(t => t.TrackNumber).FirstOrDefault()?.FilePath;
                }

                byte[]? coverBytes = await coverLoader.LoadAsync(folder, firstTrackPath).ConfigureAwait(true);
                if (coverBytes is not null)
                {
                    return await CoverService.ConvertToBitmapAsync(coverBytes, cancellationToken: cancellationToken).ConfigureAwait(true);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Eine unlesbare Folge darf die Ansicht nicht aufhalten; sie zeigt den Platzhalter.
                _logger?.Debug(() => $"Lokales Cover der Folge '{episode.Title}' nicht lesbar: {ex.Message}");
            }

            return null;
        }
    }
}
