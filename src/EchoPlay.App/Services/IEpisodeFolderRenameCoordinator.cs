using EchoPlay.LocalLibrary.Models;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Verbindet das Umbenennen der Folgenordner mit Einstellungen und Datenbank: Muster aus
    /// den Einstellungen, danach die neuen Pfade bei Folgen und Spuren.
    /// </summary>
    public interface IEpisodeFolderRenameCoordinator
    {
        /// <summary>
        /// Erstellt die Vorschau für eine Serie mit dem eingestellten Ordnermuster.
        /// </summary>
        /// <param name="seriesFolderPath">Serienordner auf dem Datenträger.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Die Vorschau, oder <see langword="null"/>, wenn der Ordner nicht existiert.</returns>
        Task<FolderRenamePlan?> PlanAsync(string seriesFolderPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Benennt um und zieht die Pfade der betroffenen Folgen und Spuren nach.
        /// </summary>
        /// <param name="seriesId">Die Serie, zu der die Ordner gehören.</param>
        /// <param name="plan">Die bestätigte Vorschau.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Was umbenannt und was übersprungen wurde.</returns>
        Task<FolderRenameResult> ExecuteAsync(Guid seriesId, FolderRenamePlan plan, CancellationToken cancellationToken = default);
    }
}
