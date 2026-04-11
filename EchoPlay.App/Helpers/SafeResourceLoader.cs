using Microsoft.UI.Xaml;
using Windows.ApplicationModel.Resources;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Liefert lokalisierte Strings aus den <c>.resw</c>-Ressourcen und fällt in Unit-Tests
    /// (ohne WinUI-Runtime / ohne <see cref="Application.Current"/>) auf einen Fallback-Wert zurück.
    /// Der <see cref="ResourceLoader"/> kann in Test-Hosts einen COM-Fehler werfen; dieser Helper
    /// kapselt den Guard, damit ViewModels in Tests nicht abstürzen.
    /// </summary>
    internal static class SafeResourceLoader
    {
        /// <summary>
        /// Lädt den lokalisierten Wert für <paramref name="key"/>. Ist die WinUI-Runtime nicht
        /// verfügbar oder tritt ein nativer Fehler auf, wird <paramref name="fallback"/> zurückgegeben.
        /// </summary>
        /// <param name="key">Der Ressourcen-Schlüssel.</param>
        /// <param name="fallback">Rückfallwert für Tests oder fehlende Einträge.</param>
        /// <returns>Der lokalisierte String oder der Fallback.</returns>
        public static string Get(string key, string fallback = "")
        {
            try
            {
                // Prüfung ob WinUI-Runtime verfügbar ist – in Tests gibt es keine App-Instanz.
                // Application.Current wirft in manchen Test-Hosts eine COMException.
                if (Application.Current is null)
                {
                    return fallback;
                }

                return ResourceLoader.GetForViewIndependentUse().GetString(key);
            }
            catch
            {
                // Nativer COM-Fehler oder fehlende WinUI-Runtime → Fallback
                return fallback;
            }
        }
    }
}
