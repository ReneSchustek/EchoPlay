using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Schreibt, was der Nutzer im Ordnerdialog der lokalen Mediathek gewählt hat: den
    /// Wurzelordner in die Einstellungen oder einen Serienordner als neue Serie.
    /// </summary>
    /// <param name="scopeFactory">Öffnet je Vorgang einen eigenen Datenbankbereich.</param>
    internal sealed class LocalLibraryFolderStore(IServiceScopeFactory scopeFactory)
    {
        /// <summary>Speichert den Wurzelordner, damit er beim nächsten Start noch vorhanden ist.</summary>
        /// <param name="rootPath">Der gewählte Ordner.</param>
        public async Task SaveRootPathAsync(string rootPath)
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            IAppSettingsDataService settingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
            AppSettings settings = await settingsService.GetAsync();
            settings.LocalLibraryRootPath = rootPath;
            await settingsService.SaveAsync(settings);
        }

        /// <summary>
        /// Legt den Ordner als neue lokale Serie an, sofern ihn noch keine Serie als Ordner führt.
        /// </summary>
        /// <param name="title">Der Ordnername, zugleich Titel der Serie.</param>
        /// <param name="folderPath">Der Pfad des Ordners.</param>
        /// <returns><see langword="true"/>, wenn eine Serie angelegt wurde.</returns>
        public async Task<bool> TryAddSeriesFolderAsync(string title, string folderPath)
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();

            IReadOnlyList<Series> allSeries = await seriesService.GetAllAsync();
            if (allSeries.Any(s => string.Equals(s.LocalFolderPath, folderPath, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            await seriesService.AddAsync(new Series { Title = title, LocalFolderPath = folderPath });
            return true;
        }

        /// <summary>Ob die Datenbank noch gar keine Serie führt, etwa nach „Bibliothek zurücksetzen".</summary>
        /// <returns><see langword="true"/> bei leerem Bestand.</returns>
        public async Task<bool> IsLibraryEmptyAsync()
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            IReadOnlyList<Series> existing = await seriesService.GetAllAsync();
            return existing.Count == 0;
        }
    }
}
