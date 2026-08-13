using EchoPlay.Core;
using EchoPlay.Data.Entities.Library;
using EchoPlay.LocalLibrary.Cover;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Beschafft das Cover einer Serie in fester Reihenfolge: erst die Datenbank, dann die
    /// <c>cover.jpg</c> im Serienordner. Wird dort eine gefunden, die noch nicht in der
    /// Datenbank steht, wandert sie hinein — die Sammelabfrage der Kacheln ist danach schneller.
    /// </summary>
    public sealed class SeriesCoverBuilder : ISeriesCoverBuilder
    {
        private readonly ICoverService? _coverService;

        /// <summary>
        /// Initialisiert den Aufbau.
        /// </summary>
        /// <param name="coverService">
        /// Zentraler Cover-Dienst für die in der Datenbank abgelegten Bilder.
        /// Ohne ihn bleibt allein die Datei im Serienordner als Quelle.
        /// </param>
        public SeriesCoverBuilder(ICoverService? coverService)
        {
            _coverService = coverService;
        }

        /// <inheritdoc/>
        public async Task<BitmapImage?> BuildAsync(Series series)
        {
            ArgumentNullException.ThrowIfNull(series);

            if (_coverService is not null)
            {
                BitmapImage? storedCover = await _coverService.GetSeriesCoverImageAsync(series.Id);

                if (storedCover is not null)
                {
                    return storedCover;
                }
            }

            return await BuildFromFolderAsync(series);
        }

        /// <inheritdoc/>
        public async Task<BitmapImage?> TryBuildAsync(Series series)
        {
            try
            {
                return await BuildAsync(series);
            }
            catch (IOException)
            {
                // Cover-Lese-Fehler ignorieren – Platzhalter bleibt
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                // Keine Leserechte – Platzhalter bleibt
                return null;
            }
        }

        /// <summary>
        /// Liest die <c>cover.jpg</c> aus dem Serienordner und legt sie zugleich in der
        /// Datenbank ab.
        /// </summary>
        private async Task<BitmapImage?> BuildFromFolderAsync(Series series)
        {
            if (series.LocalFolderPath is null)
            {
                return null;
            }

            string coverPath = Path.Combine(series.LocalFolderPath, CoverConstants.CoverFileName);

            if (!File.Exists(coverPath))
            {
                return null;
            }

            byte[] coverBytes = await File.ReadAllBytesAsync(coverPath);

            if (_coverService is not null && coverBytes.Length > 0)
            {
                await _coverService.SetSeriesCoverAsync(series.Id, coverBytes);
            }

            return await CoverService.ConvertToBitmapAsync(coverBytes);
        }
    }
}
