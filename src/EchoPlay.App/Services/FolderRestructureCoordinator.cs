using EchoPlay.App.Models;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Models;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;

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
        /// <param name="scopeFactory">Fabrik für kurzlebige DI-Scopes; jeder Datenbankzugriff läuft in einem eigenen.</param>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>
        public FolderRestructureCoordinator(IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory;
            _logger = loggerFactory.CreateLogger("FolderRestructureCoordinator");
        }

        /// <inheritdoc/>
        /// <param name="seriesFolderPath">Stammordner der Serie, dessen Unterordner ausgewertet werden.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<RestructurePreviewDisplay?> AnalyzeAsync(string seriesFolderPath, CancellationToken cancellationToken = default)
        {
            using EchoPlay.Logger.Scoping.LogScope jobScope = _logger.BeginScope(EchoPlay.App.Logging.JobScopes.FolderRestructure);

            if (string.IsNullOrWhiteSpace(seriesFolderPath) || !Directory.Exists(seriesFolderPath))
            {
                return null;
            }

            string folderPattern = await LoadFolderPatternAsync(cancellationToken);

            // Dateisystem-Zugriff in den Thread-Pool, denn Analyze öffnet IO und kann blockieren.
            RestructurePreview preview = await Task.Run(() =>
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IFolderRestructureService service =
                    scope.ServiceProvider.GetRequiredService<IFolderRestructureService>();

                // Ein abgebrochener Umbau wird zuerst zurückgespielt; sonst sähe die Vorschau
                // einen halb umgebauten Ordner.
                int recovered = service.TryRecoverPendingJournal(seriesFolderPath);
                if (recovered > 0)
                {
                    _logger.Info("Abgebrochenen Ordner-Umbau zurückgespielt: {Count} Dateien", recovered);
                }

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
        /// <param name="preview">Die zuvor erzeugte Vorschau, die jetzt tatsächlich ausgeführt wird.</param>
        /// <param name="seriesId">Die Serie, deren Folgen und Spuren die neuen Pfade bekommen.</param>
        public async Task<int> ExecuteAsync(Guid seriesId, RestructurePreviewDisplay preview, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(preview);
            RestructurePreview original = preview.Original;

            int moved = await Task.Run(() =>
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IFolderRestructureService service =
                    scope.ServiceProvider.GetRequiredService<IFolderRestructureService>();

                return service.Execute(original);
            });

            // Die Dateien liegen schon am neuen Platz. Ein Abbruch hier ließe die Datenbank auf
            // den alten Pfaden; deshalb läuft das Nachziehen ohne das Abbruch-Token zu Ende.
            await RelocateInDatabaseAsync(seriesId, MovedFiles(original)).ConfigureAwait(false);
            return moved;
        }

        /// <summary>
        /// Die Dateien der Vorschau, die nach dem Lauf am Ziel liegen und nicht mehr an der Quelle.
        /// Übersprungene Dateien (Ziel war schon belegt) fehlen.
        /// </summary>
        /// <param name="preview">Die ausgeführte Vorschau.</param>
        /// <returns>Alter und neuer Pfad je verschobener Datei.</returns>
        internal static Dictionary<string, string> MovedFiles(RestructurePreview preview)
        {
            Dictionary<string, string> moved = new(StringComparer.OrdinalIgnoreCase);
            foreach (RestructureAction action in preview.Actions)
            {
                string destination = Path.Combine(action.TargetFolderPath, action.FileName);
                if (File.Exists(destination) && !File.Exists(action.SourcePath))
                {
                    moved[action.SourcePath] = destination;
                }
            }

            return moved;
        }

        /// <summary>
        /// Schreibt die Spuren der Serie mit ihren neuen Pfaden und setzt den Ordner jeder
        /// betroffenen Folge auf den Ordner ihrer Spuren. Hörstände hängen an der Folge und
        /// bleiben unberührt.
        /// </summary>
        private async Task RelocateInDatabaseAsync(Guid seriesId, Dictionary<string, string> moved)
        {
            if (moved.Count == 0)
            {
                return;
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            ILocalTrackDataService trackService = scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();

            int relocated = 0;
            foreach (Episode episode in await episodeService.GetBySeriesIdAsync(seriesId).ConfigureAwait(false))
            {
                IReadOnlyList<LocalTrack> tracks = await trackService.GetByEpisodeIdAsync(episode.Id).ConfigureAwait(false);
                if (!tracks.Any(track => moved.ContainsKey(track.FilePath)))
                {
                    continue;
                }

                List<LocalTrack> relocatedTracks = [.. tracks.Select(track => new LocalTrack
                {
                    EpisodeId = track.EpisodeId,
                    FilePath = moved.TryGetValue(track.FilePath, out string? target) ? target : track.FilePath,
                    TrackNumber = track.TrackNumber,
                    Duration = track.Duration
                })];
                await trackService.SaveTracksForEpisodeAsync(episode.Id, relocatedTracks).ConfigureAwait(false);

                episode.LocalFolderPath = Path.GetDirectoryName(relocatedTracks[0].FilePath);
                await episodeService.UpdateAsync(episode).ConfigureAwait(false);
                relocated++;
            }

            _logger.Info("Pfade nach dem Ordner-Umbau nachgezogen: {Count} Folgen", relocated);
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
