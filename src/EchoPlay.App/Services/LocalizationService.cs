using EchoPlay.App.Helpers;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Lädt lokalisierte Strings aus den <c>.resw</c>-Ressourcendateien der App
    /// (<c>Strings/{sprache}/Resources.resw</c>). Die aktive Sprache bestimmt Windows anhand
    /// von <c>ApplicationLanguages.PrimaryLanguageOverride</c> oder der Systemsprache.
    /// </summary>
    public sealed class LocalizationService : ILocalizationService
    {
        /// <summary>
        /// Gibt den lokalisierten String für den angegebenen Schlüssel zurück.
        /// Fehlt der Schlüssel, liefert <see cref="ResourceKeyResolver"/> einen Marker (<c>[!]key</c>).
        /// </summary>
        /// <param name="key">Der Ressourcenschlüssel, z.B. <c>"NavStartseite.Content"</c>.</param>
        /// <returns>Der lokalisierte String oder ein Fehlmarker, wenn der Schlüssel fehlt.</returns>
        // Zugriff bewusst über SafeResourceLoader (Windows-App-SDK-Loader): der WinRT-Loader
        // crasht im unpackaged-Betrieb. Siehe SafeResourceLoader.
        public string Get(string key) => ResourceKeyResolver.Resolve(key, k => SafeResourceLoader.Get(k));
    }
}
