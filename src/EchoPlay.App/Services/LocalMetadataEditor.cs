using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Parsing;
using EchoPlay.Logger.Abstractions;
using EchoPlay.TagManager.Abstractions;
using EchoPlay.TagManager.Models;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Berichtigt Folgen und Serien der lokalen Mediathek. Reihenfolge beim Speichern einer
    /// Folge: Tags, Datenbank, zuletzt der Ordner. Das Umbenennen zieht die Pfade in der
    /// Datenbank selbst nach und muss deshalb nach dem Speichern der übrigen Angaben laufen,
    /// sonst überschriebe das Speichern den neuen Ordnerpfad mit dem alten.
    /// </summary>
    public sealed class LocalMetadataEditor : ILocalMetadataEditor
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IEpisodeFolderRenameCoordinator _folderRename;
        private readonly ILogger _logger;

        /// <summary>
        /// Richtet den Dienst ein.
        /// </summary>
        /// <param name="scopeFactory">Liefert Datendienste und den Tag-Dienst je Vorgang.</param>
        /// <param name="folderRename">Benennt den Folgenordner um und zieht die Pfade nach.</param>
        /// <param name="loggerFactory">Liefert das Protokoll.</param>
        public LocalMetadataEditor(
            IServiceScopeFactory scopeFactory,
            IEpisodeFolderRenameCoordinator folderRename,
            ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _scopeFactory = scopeFactory;
            _folderRename = folderRename;
            _logger = loggerFactory.CreateLogger(nameof(LocalMetadataEditor));
        }

        /// <inheritdoc/>
        public async Task<EpisodeEditData?> LoadEpisodeAsync(Guid episodeId, CancellationToken cancellationToken = default)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            Episode? episode = await scope.ServiceProvider.GetRequiredService<IEpisodeDataService>()
                .GetByIdAsync(episodeId, cancellationToken).ConfigureAwait(false);
            if (episode is null)
            {
                return null;
            }

            IReadOnlyList<LocalTrack> tracks = await scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>()
                .GetByEpisodeIdAsync(episodeId, cancellationToken).ConfigureAwait(false);
            string? artist = tracks.Count == 0
                ? null
                : await ReadArtistAsync(scope.ServiceProvider.GetRequiredService<ITagService>(), tracks[0].FilePath).ConfigureAwait(false);

            return new EpisodeEditData(episode.Id, episode.Title, episode.EpisodeNumber, artist, episode.ReleaseDate?.Year);
        }

        /// <inheritdoc/>
        public async Task<EpisodeEditOutcome> SaveEpisodeAsync(EpisodeEditData edit, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(edit);

            using IServiceScope scope = _scopeFactory.CreateScope();
            IEpisodeDataService episodeService = scope.ServiceProvider.GetRequiredService<IEpisodeDataService>();
            Episode? episode = await episodeService.GetByIdAsync(edit.EpisodeId, cancellationToken).ConfigureAwait(false);
            if (episode is null)
            {
                return new EpisodeEditOutcome(0, 0, FolderRenamed: false);
            }

            IReadOnlyList<LocalTrack> tracks = await scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>()
                .GetByEpisodeIdAsync(episode.Id, cancellationToken).ConfigureAwait(false);
            (int written, int failed) = await WriteTagsAsync(scope.ServiceProvider.GetRequiredService<ITagService>(), tracks, edit).ConfigureAwait(false);

            bool numberChanged = episode.EpisodeNumber != edit.Number;
            bool titleChanged = !string.Equals(episode.Title, edit.Title, StringComparison.Ordinal);
            episode.Title = edit.Title;
            episode.EpisodeNumber = edit.Number;
            episode.ReleaseDate = NewReleaseDate(episode.ReleaseDate, edit.Year);
            await episodeService.UpdateAsync(episode, cancellationToken).ConfigureAwait(false);

            bool renamed = (numberChanged || titleChanged)
                && await RenameFolderAsync(scope, episode, cancellationToken).ConfigureAwait(false);

            _logger.Info(
                "Folge bearbeitet: \"{Title}\", Tags in {Written} Dateien, {Failed} fehlgeschlagen, Ordner umbenannt: {Renamed}",
                edit.Title, written, failed, renamed);
            return new EpisodeEditOutcome(written, failed, renamed);
        }

        /// <inheritdoc/>
        public async Task<bool> SaveSeriesTitleAsync(Guid seriesId, string title, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(title);

            using IServiceScope scope = _scopeFactory.CreateScope();
            ISeriesDataService seriesService = scope.ServiceProvider.GetRequiredService<ISeriesDataService>();
            Series? series = await seriesService.GetByIdAsync(seriesId, cancellationToken).ConfigureAwait(false);
            if (series is null)
            {
                return false;
            }

            series.Title = title.Trim();
            await seriesService.UpdateAsync(series, cancellationToken).ConfigureAwait(false);
            _logger.Info("Serie umbenannt: \"{Title}\"", series.Title);
            return true;
        }

        /// <summary>
        /// Das Jahr bleibt mit Monat und Tag stehen, wenn es sich nicht ändert; ein neues Jahr
        /// beginnt am 1. Januar, weil der Dialog nur das Jahr kennt.
        /// </summary>
        internal static DateTime? NewReleaseDate(DateTime? current, int? year)
        {
            if (year is null)
            {
                return null;
            }

            return current?.Year == year ? current : new DateTime(year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        /// <summary>
        /// Schreibt Album, Interpret und Jahr in jede Datei der Folge. Der Spurtitel bleibt, er
        /// nennt das Kapitel. Eine Folgennummer kennt das Tag-Format nicht; sie steht im
        /// Ordnernamen.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "TagLib wirft je nach Dateiformat eigene Ausnahmetypen. Eine einzelne nicht schreibbare Datei darf das Speichern der übrigen nicht verhindern; sie wird gezählt und protokolliert.")]
        private async Task<(int Written, int Failed)> WriteTagsAsync(ITagService tagService, IReadOnlyList<LocalTrack> tracks, EpisodeEditData edit)
        {
            AudioTag tag = new()
            {
                Album = edit.Title,
                Artist = string.IsNullOrWhiteSpace(edit.Artist) ? null : edit.Artist.Trim(),
                Year = edit.Year is int year ? (uint)year : null
            };

            int written = 0;
            int failed = 0;
            foreach (LocalTrack track in tracks)
            {
                try
                {
                    await tagService.WriteAsync(track.FilePath, tag).ConfigureAwait(false);
                    written++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.Warning("Tags nicht geschrieben: {Path} – {Reason}", EchoPlay.Core.Logging.PathRedactor.Redact(track.FilePath), ex.Message);
                }
            }

            return (written, failed);
        }

        /// <summary>
        /// Benennt den Ordner der Folge nach dem eingestellten Muster um. Bleibt stehen, wenn die
        /// Folge keinen eigenen Ordner hat (flache Serie), keine Nummer trägt oder das Muster
        /// keinen Namen bilden kann.
        /// </summary>
        private async Task<bool> RenameFolderAsync(IServiceScope scope, Episode episode, CancellationToken cancellationToken)
        {
            string? folder = episode.LocalFolderPath;
            if (folder is null || episode.EpisodeNumber is not int number || !Directory.Exists(folder))
            {
                return false;
            }

            string? seriesFolder = await FindParentSeriesFolderAsync(scope, episode, folder, cancellationToken).ConfigureAwait(false);
            if (seriesFolder is null)
            {
                return false;
            }

            AppSettings settings = await scope.ServiceProvider.GetRequiredService<IAppSettingsDataService>()
                .GetAsync(cancellationToken).ConfigureAwait(false);
            string? target = EpisodeFolderNaming.Render(settings.EpisodeFolderPattern, number, episode.Title);
            if (target is null || string.Equals(target, Path.GetFileName(folder), StringComparison.Ordinal))
            {
                return false;
            }

            FolderRenamePlan plan = new()
            {
                SeriesFolderPath = seriesFolder,
                Items = new[] { new FolderRenameItem(folder, target, FolderRenameStatus.Rename, TitleFromTag: false) }
            };
            FolderRenameResult result = await _folderRename.ExecuteAsync(episode.SeriesId, plan, cancellationToken).ConfigureAwait(false);
            return result.Moves.Count > 0;
        }

        /// <summary>
        /// Der Ordner über dem Folgenordner; <see langword="null"/>, wenn der Folgenordner selbst
        /// der Serienordner ist (flache Serie) oder keinen übergeordneten hat.
        /// </summary>
        private static async Task<string?> FindParentSeriesFolderAsync(
            IServiceScope scope, Episode episode, string folder, CancellationToken cancellationToken)
        {
            Series? series = await scope.ServiceProvider.GetRequiredService<ISeriesDataService>()
                .GetByIdAsync(episode.SeriesId, cancellationToken).ConfigureAwait(false);
            string? seriesFolder = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder));
            bool isFlat = string.Equals(
                Path.TrimEndingDirectorySeparator(folder),
                Path.TrimEndingDirectorySeparator(series?.LocalFolderPath ?? string.Empty),
                StringComparison.OrdinalIgnoreCase);

            return isFlat ? null : seriesFolder;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Der Interpret ist nur ein Vorschlag im Dialog; eine unlesbare Datei lässt das Feld leer, statt das Bearbeiten zu verhindern.")]
        private async Task<string?> ReadArtistAsync(ITagService tagService, string filePath)
        {
            try
            {
                AudioTag tag = await tagService.ReadAsync(filePath).ConfigureAwait(false);
                return tag.Artist;
            }
            catch (Exception ex)
            {
                _logger.Debug(() => $"Interpret nicht lesbar: {ex.Message}");
                return null;
            }
        }
    }
}
