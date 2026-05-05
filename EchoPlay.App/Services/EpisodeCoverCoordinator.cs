using EchoPlay.App.Helpers;
using EchoPlay.App.Models;
using EchoPlay.App.ViewModels;
using EchoPlay.Core;
using EchoPlay.Core.Security;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Cover;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Standard-Implementierung von <see cref="IEpisodeCoverCoordinator"/>.
    /// Singleton: nutzt eigene DI-Scopes und einen statischen <see cref="HttpClient"/>,
    /// damit die wiederholte Instanziierung des Transient-ViewModels keine Sockets
    /// erschöpft.
    /// </summary>

    public sealed class EpisodeCoverCoordinator : IEpisodeCoverCoordinator
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ICoverSearchService _coverSearchService;
        private readonly CoverService _coverService;
        private readonly IConfirmationDialogService _confirmationDialogService;
        private readonly IErrorDialogService _errorDialogService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILocalizationService _localizationService;

        /// <summary>
        /// Initialisiert den Koordinator mit den benötigten Diensten.
        /// </summary>

        public EpisodeCoverCoordinator(
            IServiceScopeFactory scopeFactory,
            ICoverSearchService coverSearchService,
            CoverService coverService,
            IConfirmationDialogService confirmationDialogService,
            IErrorDialogService errorDialogService,
            IHttpClientFactory httpClientFactory,
            ILocalizationService localizationService)
        {
            _scopeFactory = scopeFactory;
            _coverSearchService = coverSearchService;
            _coverService = coverService;
            _confirmationDialogService = confirmationDialogService;
            _errorDialogService = errorDialogService;
            _httpClientFactory = httpClientFactory;
            _localizationService = localizationService;
        }

        /// <inheritdoc/>


        /// <param name="query">Parameter <c>query</c>.</param>
        /// <param name="ct">Parameter <c>ct</c>.</param>
        public async Task<IReadOnlyList<CoverSearchHit>> SearchCoversAsync(string query, CancellationToken ct)
        {
            IReadOnlyList<CoverSearchResult> results = await _coverSearchService.SearchAsync(query, ct);
            List<CoverSearchHit> hits = new(results.Count);
            foreach (CoverSearchResult r in results)
            {
                hits.Add(CoverSearchHit.From(r));
            }
            return hits;
        }

        /// <inheritdoc/>

        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        /// <param name="card">Parameter <c>card</c>.</param>
        /// <param name="bytes">Parameter <c>bytes</c>.</param>
        public async Task ApplySeriesCoverFromBytesAsync(LocalArtistCardViewModel card, byte[] bytes, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(card);
            if (!await ConfirmOverwriteIfNeededAsync(card.CoverImage is not null, cancellationToken))
            {
                return;
            }

            await _coverService.SetSeriesCoverAsync(card.SeriesId, bytes, cancellationToken: cancellationToken);
            await SaveCoverToDirectoryAsync(card.LocalFolderPath, bytes, cancellationToken);

            card.CoverImage = await CoverService.ConvertToBitmapAsync(bytes, cancellationToken);
        }

        /// <inheritdoc/>

        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        /// <param name="card">Parameter <c>card</c>.</param>
        /// <param name="bytes">Parameter <c>bytes</c>.</param>
        public async Task ApplyEpisodeCoverFromBytesAsync(LocalEpisodeCardViewModel card, byte[] bytes, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(card);
            if (!await ConfirmOverwriteIfNeededAsync(card.CoverImage is not null, cancellationToken))
            {
                return;
            }

            await _coverService.SetEpisodeCoverAsync(card.EpisodeId, bytes, cancellationToken: cancellationToken);
            await SaveCoverToDirectoryAsync(card.FolderPath, bytes, cancellationToken);

            card.CoverImage = await CoverService.ConvertToBitmapAsync(bytes, cancellationToken);
        }

        /// <inheritdoc/>

        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        /// <param name="card">Parameter <c>card</c>.</param>
        /// <param name="hit">Parameter <c>hit</c>.</param>
        public async Task ApplySelectedSeriesCoverAsync(LocalArtistCardViewModel card, CoverSearchHit hit, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(hit);
            byte[]? bytes = await DownloadCoverBytesAsync(hit.FullUrl, cancellationToken);
            if (bytes is null)
            {
                await _errorDialogService.ShowAsync(_localizationService.Get("CoverDownloadFailedTitle"), _localizationService.Get("CoverDownloadFailedMessage"), cancellationToken);
                return;
            }

            await ApplySeriesCoverFromBytesAsync(card, bytes, cancellationToken);
        }

        /// <inheritdoc/>

        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        /// <param name="card">Parameter <c>card</c>.</param>
        /// <param name="hit">Parameter <c>hit</c>.</param>
        public async Task ApplySelectedEpisodeCoverAsync(LocalEpisodeCardViewModel card, CoverSearchHit hit, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(hit);
            byte[]? bytes = await DownloadCoverBytesAsync(hit.FullUrl, cancellationToken);
            if (bytes is null)
            {
                await _errorDialogService.ShowAsync(_localizationService.Get("CoverDownloadFailedTitle"), _localizationService.Get("CoverDownloadFailedMessage"), cancellationToken);
                return;
            }

            await ApplyEpisodeCoverFromBytesAsync(card, bytes, cancellationToken);
        }

        /// <summary>
        /// Holt eine Bestätigung vom Nutzer ein, wenn die Karte bereits ein Cover hat.
        /// Liefert <see langword="true"/>, wenn die Operation fortgesetzt werden darf.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        /// <param name="hasExistingCover">Parameter <c>hasExistingCover</c>.</param>
        private async Task<bool> ConfirmOverwriteIfNeededAsync(bool hasExistingCover, CancellationToken cancellationToken = default)
        {
            if (!hasExistingCover)
            {
                return true;
            }

            return await _confirmationDialogService.ConfirmAsync(SafeResourceLoader.Get("EpisodeCoverOverwriteTitle"), SafeResourceLoader.Get("EpisodeCoverOverwriteMessage"), cancellationToken);
        }

        /// <summary>
        /// Schreibt das Cover als <c>cover.jpg</c> in den Ordner, wenn die AppSettings-Option
        /// <c>SaveCoverToDirectory</c> aktiv ist. Datei-Speicherung ist optional – das Cover
        /// wurde bereits in der DB persistiert.
        /// </summary>

        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        /// <param name="folderPath">Parameter <c>folderPath</c>.</param>
        /// <param name="bytes">Parameter <c>bytes</c>.</param>
        private async Task SaveCoverToDirectoryAsync(string? folderPath, byte[] bytes, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                return;
            }

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IAppSettingsDataService settingsService = scope.ServiceProvider
                    .GetRequiredService<IAppSettingsDataService>();
                AppSettings settings = await settingsService.GetAsync(cancellationToken);

                if (!settings.SaveCoverToDirectory)
                {
                    return;
                }

                string coverPath = Path.Combine(folderPath, CoverConstants.CoverFileName);

                // Defense-in-Depth: Cover darf nur in den uebergebenen Folder geschrieben werden,
                // nie ausserhalb (Symlink-Escape oder kuenstlicher CoverFileName-Wert).
                if (!SecurePathHelper.IsPathInside(coverPath, folderPath))
                {
                    return;
                }

                await File.WriteAllBytesAsync(coverPath, bytes, cancellationToken);
            }
            catch (IOException)
            {
                // Datei-Speicherung ist optional – DB hat das Cover bereits
            }
            catch (UnauthorizedAccessException)
            {
                // Schreibrechte fehlen – kein kritisches Problem
            }
        }

        /// <summary>
        /// Lädt Bilddaten von der angegebenen URL als Byte-Array herunter.
        /// Liefert <see langword="null"/> bei Netzwerk- oder HTTP-Fehlern – kein Throw.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        /// <param name="url">Parameter <c>url</c>.</param>
        private async Task<byte[]?> DownloadCoverBytesAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                HttpClient client = _httpClientFactory.CreateClient("CoverDownload");
                return await client.GetByteArrayAsync(new Uri(url, UriKind.Absolute), cancellationToken);
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (TaskCanceledException)
            {
                return null;
            }
        }
    }
}
