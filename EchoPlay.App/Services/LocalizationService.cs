using Windows.ApplicationModel.Resources;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Lädt lokalisierte Strings aus den <c>.resw</c>-Ressourcendateien der App.
    /// Der <see cref="ResourceLoader"/> greift auf <c>Strings/{sprache}/Resources.resw</c> zu –
    /// die aktive Sprache wird von Windows anhand von <c>ApplicationLanguages.PrimaryLanguageOverride</c>
    /// oder der Systemsprache bestimmt.
    /// </summary>
    public sealed class LocalizationService : ILocalizationService
    {
        // GetForViewIndependentUse kann thread-sicher von jedem Thread aufgerufen werden
        private readonly ResourceLoader _loader = ResourceLoader.GetForViewIndependentUse();

        /// <summary>
        /// Gibt den lokalisierten String für den angegebenen Schlüssel zurück.
        /// Ist der Schlüssel nicht vorhanden, gibt <see cref="ResourceLoader.GetString"/> einen leeren String zurück.
        /// </summary>
        /// <param name="key">Der Ressourcenschlüssel, z.B. <c>"NavStartseite.Content"</c>.</param>
        /// <returns>Der lokalisierte String oder <see cref="string.Empty"/> wenn der Schlüssel fehlt.</returns>
        public string Get(string key) => _loader.GetString(key);
    }
}
