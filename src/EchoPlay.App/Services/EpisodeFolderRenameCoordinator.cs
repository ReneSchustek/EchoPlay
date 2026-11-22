using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Models;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Standard-Implementierung von <see cref="IEpisodeFolderRenameCoordinator"/>. Singleton;
    /// jeder Aufruf holt sich eigene Scopes, weil Dateidienst und Datenbankdienste Scoped sind.
    /// </summary>
    /// <remarks>
    /// Hörstände hängen an der Folge, nicht am Pfad, und überstehen das Umbenennen. Die Spuren
    /// dagegen speichern ihren Dateipfad; sie werden mit dem neuen Pfad neu geschrieben.
    /// </remarks>
    public sealed class EpisodeFolderRenameCoordinator : IEpisodeFolderRenameCoordinator
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;

        /// <summary>
        /// Richtet den Koordinator ein.
        /// </summary>
        /// <param name="scopeFactory">Fabrik für kurzlebige Scopes.</param>
        /// <param name="loggerFactory">Fabrik für den Protokollkanal.</param>
        public EpisodeFolderRenameCoordinator(IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(scopeFactory);
            ArgumentNullException.ThrowIfNull(loggerFactory);

            _scopeFactory = scopeFactory;
            _logger = loggerFactory.CreateLogger(nameof(EpisodeFolderRenameCoordinator));
        }

        /// <inheritdoc/>
        public async Task<FolderRenamePlan?> PlanAsync(string seriesFolderPath, CancellationToken cancellationToken = default)
        {
            using EchoPlay.Logger.Scoping.LogScope jobScope = _logger.BeginScope(EchoPlay.App.Logging.JobScopes.FolderRename);

            if (string.IsNullOrWhiteSpace(seriesFolderPath) || !Directory.Exists(seriesFolderPath))
            {
                return null;
            }

            string folderPattern = await LoadFolderPatternAsync(cancellationToken).ConfigureAwait(false);

            // Tags lesen heißt Dateien öffnen; das gehört nicht auf den Oberflächen-Thread.
            return await Task.Run(() =>
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                return scope.ServiceProvider.GetRequiredService<IEpisodeFolderRenameService>()
                    .Plan(seriesFolderPath, folderPattern);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<FolderRenameResult> ExecuteAsync(Guid seriesId, FolderRenamePlan plan, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(plan);
            using EchoPlay.Logger.Scoping.LogScope jobScope = _logger.BeginScope(EchoPlay.App.Logging.JobScopes.FolderRename);

            FolderRenameResult result = await Task.Run(() =>
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                return scope.ServiceProvider.GetRequiredService<IEpisodeFolderRenameService>().Execute(plan);
            }, cancellationToken).ConfigureAwait(false);

            // Die Ordner sind schon umbenannt. Ein Abbruch hier ließe die Datenbank auf den alten
            // Pfaden stehen; deshalb läuft das Nachziehen ohne das Abbruch-Token zu Ende.
            await RelocateInDatabaseAsync(seriesId, result.Moves).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Setzt bei den Folgen der Serie den neuen Ordnerpfad und schreibt ihre Spuren mit den
        /// neuen Dateipfaden.
        /// </summary>
        private async Task RelocateInDatabaseAsync(Guid seriesId, IReadOnlyList<(string OldPath, string NewPath)> moves)
        {
            if (moves.Count == 0)
            {
                return;
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            ILocalTrackDataService trackService = scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();

            Dictionary<string, string> newPathByOld = moves.ToDictionary(
                move => move.OldPath, move => move.NewPath, StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<Episode> episodes = await episodeService.GetBySeriesIdAsync(seriesId).ConfigureAwait(false);

            int relocated = 0;
            foreach (Episode episode in episodes)
            {
                if (episode.LocalFolderPath is null || !newPathByOld.TryGetValue(episode.LocalFolderPath, out string? newFolder))
                {
                    continue;
                }

                await RelocateEpisodeAsync(episodeService, trackService, episode, newFolder).ConfigureAwait(false);
                relocated++;
            }

            _logger.Info("Pfade nach dem Umbenennen nachgezogen: {Count} Folgen", relocated);
        }

        /// <summary>
        /// Schreibt den neuen Ordner der Folge und die Dateipfade ihrer Spuren.
        /// </summary>
        private static async Task RelocateEpisodeAsync(
            IEpisodeDataService episodeService,
            ILocalTrackDataService trackService,
            Episode episode,
            string newFolder)
        {
            string oldFolder = episode.LocalFolderPath!;
            episode.LocalFolderPath = newFolder;
            await episodeService.UpdateAsync(episode).ConfigureAwait(false);

            IReadOnlyList<LocalTrack> tracks = await trackService.GetByEpisodeIdAsync(episode.Id).ConfigureAwait(false);
            List<LocalTrack> moved = [.. tracks.Select(track => new LocalTrack
            {
                EpisodeId = track.EpisodeId,
                FilePath = RelocatePath(track.FilePath, oldFolder, newFolder),
                TrackNumber = track.TrackNumber,
                Duration = track.Duration
            })];
            await trackService.SaveTracksForEpisodeAsync(episode.Id, moved).ConfigureAwait(false);
        }

        /// <summary>
        /// Ersetzt den Ordneranteil eines Dateipfads. Liegt die Datei nicht in diesem Ordner,
        /// bleibt der Pfad, wie er ist.
        /// </summary>
        /// <param name="filePath">Der gespeicherte Dateipfad.</param>
        /// <param name="oldFolder">Der Ordner vor dem Umbenennen.</param>
        /// <param name="newFolder">Der Ordner danach.</param>
        /// <returns>Der Dateipfad im neuen Ordner.</returns>
        internal static string RelocatePath(string filePath, string oldFolder, string newFolder)
        {
            string prefix = Path.TrimEndingDirectorySeparator(oldFolder) + Path.DirectorySeparatorChar;
            return filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(newFolder, filePath[prefix.Length..])
                : filePath;
        }

        private async Task<string> LoadFolderPatternAsync(CancellationToken cancellationToken)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            AppSettings settings = await scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>()
                .GetAsync(cancellationToken).ConfigureAwait(false);
            return settings.EpisodeFolderPattern;
        }
    }
}
