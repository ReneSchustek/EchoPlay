using EchoPlay.Data.Entities.Settings;

namespace EchoPlay.Data.Services.Interfaces
{
    /// <summary>
    /// Definiert den Vertrag für den Zugriff auf die anwendungsweiten Einstellungen.
    /// Die Einstellungen werden als einzelne Zeile in der Datenbank gespeichert.
    /// Falls noch keine Zeile existiert, wird beim ersten Zugriff automatisch eine Standardinstanz angelegt.
    /// </summary>
    public interface IAppSettingsDataService
    {
        /// <summary>
        /// Lädt die aktuellen Anwendungseinstellungen.
        /// Falls noch keine Einstellungen gespeichert wurden, wird eine neue Instanz mit Standardwerten erstellt,
        /// persistiert und zurückgegeben.
        /// </summary>
        /// <returns>Die aktuellen <see cref="AppSettings"/>.</returns>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task<AppSettings> GetAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Speichert die übergebenen Einstellungen dauerhaft.
        /// </summary>
        /// <param name="settings">Die zu persistierenden Einstellungen.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
    }
}
