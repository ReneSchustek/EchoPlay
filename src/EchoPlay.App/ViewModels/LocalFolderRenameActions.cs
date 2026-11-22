using EchoPlay.App.Services;
using EchoPlay.LocalLibrary.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Die Aktion „Ordner nach Tags umbenennen" der lokalen Mediathek: Vorschau holen,
    /// umbenennen, danach die Mediathek neu laden.
    /// </summary>
    public sealed class LocalFolderRenameActions
    {
        private readonly IEpisodeFolderRenameCoordinator? _coordinator;
        private readonly LocalArtistsViewModel _artistsVM;
        private readonly Func<Task> _reloadLibrary;

        /// <summary>
        /// Richtet die Aktion ein.
        /// </summary>
        /// <param name="coordinator">Der Koordinator; <see langword="null"/> in Tests ohne Dateizugriff.</param>
        /// <param name="artistsVM">Die Serienliste, aus der der Ordner einer Serie kommt.</param>
        /// <param name="reloadLibrary">Lädt die Mediathek nach dem Umbenennen neu.</param>
        public LocalFolderRenameActions(
            IEpisodeFolderRenameCoordinator? coordinator,
            LocalArtistsViewModel artistsVM,
            Func<Task> reloadLibrary)
        {
            ArgumentNullException.ThrowIfNull(artistsVM);
            ArgumentNullException.ThrowIfNull(reloadLibrary);

            _coordinator = coordinator;
            _artistsVM = artistsVM;
            _reloadLibrary = reloadLibrary;
        }

        /// <summary>
        /// Erstellt die Vorschau für eine Serie.
        /// </summary>
        /// <param name="seriesId">Die gewählte Serie.</param>
        /// <returns>Die Vorschau, oder <see langword="null"/>, wenn die Serie keinen Ordner hat.</returns>
        public async Task<FolderRenamePlan?> PlanAsync(Guid seriesId)
        {
            if (_coordinator is null)
            {
                return null;
            }

            using IDisposable userAction = UserActionScope.BeginUserAction("PlanFolderRename");
            string? folder = _artistsVM.AllArtists.FirstOrDefault(card => card.SeriesId == seriesId)?.LocalFolderPath;
            return folder is null ? null : await _coordinator.PlanAsync(folder);
        }

        /// <summary>
        /// Benennt nach der bestätigten Vorschau um und lädt die Mediathek neu, wenn sich etwas
        /// geändert hat.
        /// </summary>
        /// <param name="seriesId">Die Serie der Vorschau.</param>
        /// <param name="plan">Die bestätigte Vorschau.</param>
        /// <returns>Das Ergebnis des Laufs.</returns>
        public async Task<FolderRenameResult> ExecuteAsync(Guid seriesId, FolderRenamePlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            if (_coordinator is null)
            {
                return new FolderRenameResult([], 0);
            }

            using IDisposable userAction = UserActionScope.BeginUserAction("ExecuteFolderRename");
            FolderRenameResult result = await _coordinator.ExecuteAsync(seriesId, plan);
            if (result.Moves.Count > 0)
            {
                await _reloadLibrary();
            }

            return result;
        }
    }
}
