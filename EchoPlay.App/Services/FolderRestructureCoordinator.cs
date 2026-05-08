using EchoPlay.App.Models;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Models;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
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
        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert den Koordinator mit der Scope-Fabrik der Anwendung.
        /// </summary>
        /// <param name="scopeFactory">Parameter <c>scopeFactory</c>.</param>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>
        public FolderRestructureCoordinator(IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory;
            _logger = loggerFactory.CreateLogger("FolderRestructureCoordinator");
        }

        /// <inheritdoc/>
        /// <param name="seriesFolderPath">Parameter <c>seriesFolderPath</c>.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<RestructurePreviewDisplay?> AnalyzeAsync(string seriesFolderPath, CancellationToken cancellationToken = default)
        {
            using EchoPlay.Logger.Scoping.LogScope jobScope = _logger.BeginScope(EchoPlay.App.Logging.JobScopes.FolderRestructure);

            if (string.IsNullOrWhiteSpace(seriesFolderPath) || !Directory.Exists(seriesFolderPath))
            {
                return null;
            }

            string folderPattern = await LoadFolderPatternAsync(cancellationToken);

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
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        /// <param name="preview">Parameter <c>preview</c>.</param>
        public async Task<int> ExecuteAsync(RestructurePreviewDisplay preview, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(preview);
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
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>

        private async Task<string> LoadFolderPatternAsync(CancellationToken cancellationToken = default)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IAppSettingsDataService settingsService =
                scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>();
            AppSettings appSettings = await settingsService.GetAsync(cancellationToken);
            return appSettings.EpisodeFolderPattern;
        }
    }
}
