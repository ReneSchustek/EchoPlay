using EchoPlay.App.Models;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Models;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Standard-Implementierung von <see cref="IFolderRestructureCoordinator"/>.
    /// Singleton: nutzt pro Aufruf eigene DI-Scopes für AppSettings und den
    /// LocalLibrary-Restructure-Service, weil beide als Scoped registriert sind.
    /// </summary>
    public sealed class FolderRestructureCoordinator : IFolderRestructureCoordinator
    {
        private readonly IServiceScopeFactory _scopeFactory;

        /// <summary>
        /// Initialisiert den Koordinator mit der Scope-Fabrik der Anwendung.
        /// </summary>
        public FolderRestructureCoordinator(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        /// <inheritdoc/>
        public async Task<RestructurePreviewDisplay?> AnalyzeAsync(string seriesFolderPath)
        {
            if (string.IsNullOrWhiteSpace(seriesFolderPath) || !Directory.Exists(seriesFolderPath))
            {
                return null;
            }

            string folderPattern = await LoadFolderPatternAsync();

            // Dateisystem-Zugriff in den Thread-Pool – Analyze öffnet IO und kann blockieren.
            RestructurePreview preview = await Task.Run(() =>
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IFolderRestructureService service =
                    scope.ServiceProvider.GetRequiredService<IFolderRestructureService>();

                return service.Analyze(seriesFolderPath, folderPattern);
            });

            if (preview.IsEmpty)
            {
                return null;
            }

            return new RestructurePreviewDisplay(preview);
        }

        /// <inheritdoc/>
        public async Task<int> ExecuteAsync(RestructurePreviewDisplay preview)
        {
            RestructurePreview original = preview.Original;

            return await Task.Run(() =>
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IFolderRestructureService service =
                    scope.ServiceProvider.GetRequiredService<IFolderRestructureService>();

                return service.Execute(original);
            });
        }

        /// <summary>
        /// Lädt das aktuelle Ordnermuster aus den AppSettings.
        /// </summary>
        private async Task<string> LoadFolderPatternAsync()
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService =
                scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
            AppSettings appSettings = await settingsService.GetAsync();
            return appSettings.EpisodeFolderPattern;
        }
    }
}
