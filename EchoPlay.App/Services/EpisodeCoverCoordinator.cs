using EchoPlay.App.Models;
using EchoPlay.App.ViewModels;
using EchoPlay.Core;
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

        /// <summary>
        /// Initialisiert den Koordinator mit den benötigten Diensten.
        /// </summary>
        public EpisodeCoverCoordinator(
            IServiceScopeFactory scopeFactory,
            ICoverSearchService coverSearchService,
            CoverService coverService,
            IConfirmationDialogService confirmationDialogService,
            IErrorDialogService errorDialogService,
            IHttpClientFactory httpClientFactory)
        {
            _scopeFactory              = scopeFactory;
            _coverSearchService        = coverSearchService;
            _coverService              = coverService;
            _confirmationDialogService = confirmationDialogService;
            _errorDialogService        = errorDialogService;
            _httpClientFactory         = httpClientFactory;
        }

        /// <inheritdoc/>
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
        public async Task ApplySeriesCoverFromBytesAsync(LocalArtistCardViewModel card, byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(card);
            if (!await ConfirmOverwriteIfNeededAsync(card.CoverImage is not null))
            {
                return;
            }

            await _coverService.SetSeriesCoverAsync(card.SeriesId, bytes);
            await SaveCoverToDirectoryAsync(card.LocalFolderPath, bytes);

            card.CoverImage = await CoverService.ConvertToBitmapAsync(bytes);
        }

        /// <inheritdoc/>
        public async Task ApplyEpisodeCoverFromBytesAsync(LocalEpisodeCardViewModel card, byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(card);
            if (!await ConfirmOverwriteIfNeededAsync(card.CoverImage is not null))
            {
                return;
            }

            await _coverService.SetEpisodeCoverAsync(card.EpisodeId, bytes);
            await SaveCoverToDirectoryAsync(card.FolderPath, bytes);

            card.CoverImage = await CoverService.ConvertToBitmapAsync(bytes);
        }

        /// <inheritdoc/>
        public async Task ApplySelectedSeriesCoverAsync(LocalArtistCardViewModel card, CoverSearchHit hit)
        {
            ArgumentNullException.ThrowIfNull(hit);
            byte[]? bytes = await DownloadCoverBytesAsync(hit.FullUrl);
            if (bytes is null)
            {
                await _errorDialogService.ShowAsync(
                    "Download fehlgeschlagen",
                    "Das Cover konnte nicht heruntergeladen werden. Bitte versuche es später erneut.");
                return;
            }

            await ApplySeriesCoverFromBytesAsync(card, bytes);
        }

        /// <inheritdoc/>
        public async Task ApplySelectedEpisodeCoverAsync(LocalEpisodeCardViewModel card, CoverSearchHit hit)
        {
            ArgumentNullException.ThrowIfNull(hit);
            byte[]? bytes = await DownloadCoverBytesAsync(hit.FullUrl);
            if (bytes is null)
            {
                await _errorDialogService.ShowAsync(
                    "Download fehlgeschlagen",
                    "Das Cover konnte nicht heruntergeladen werden. Bitte versuche es später erneut.");
                return;
            }

            await ApplyEpisodeCoverFromBytesAsync(card, bytes);
        }

        /// <summary>
        /// Holt eine Bestätigung vom Nutzer ein, wenn die Karte bereits ein Cover hat.
        /// Liefert <see langword="true"/>, wenn die Operation fortgesetzt werden darf.
        /// </summary>
        private async Task<bool> ConfirmOverwriteIfNeededAsync(bool hasExistingCover)
        {
            if (!hasExistingCover)
            {
                return true;
            }

            return await _confirmationDialogService.ConfirmAsync(
                "Cover überschreiben",
                "Diese Serie hat bereits ein Cover. Soll es ersetzt werden?");
        }

        /// <summary>
        /// Schreibt das Cover als <c>cover.jpg</c> in den Ordner, wenn die AppSettings-Option
        /// <c>SaveCoverToDirectory</c> aktiv ist. Datei-Speicherung ist optional – das Cover
        /// wurde bereits in der DB persistiert.
        /// </summary>
        private async Task SaveCoverToDirectoryAsync(string? folderPath, byte[] bytes)
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
                AppSettings settings = await settingsService.GetAsync();

                if (!settings.SaveCoverToDirectory)
                {
                    return;
                }

                string coverPath = Path.Combine(folderPath, CoverConstants.CoverFileName);
                await File.WriteAllBytesAsync(coverPath, bytes);
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
        private async Task<byte[]?> DownloadCoverBytesAsync(string url)
        {
            try
            {
                HttpClient client = _httpClientFactory.CreateClient("CoverDownload");
                return await client.GetByteArrayAsync(new Uri(url, UriKind.Absolute));
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
