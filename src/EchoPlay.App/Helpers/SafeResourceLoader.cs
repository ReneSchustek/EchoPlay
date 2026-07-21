using Microsoft.UI.Xaml;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Zentrale, unpackaged-taugliche Quelle für lokalisierte <c>.resw</c>-Strings.
    /// </summary>
    /// <remarks>
    /// Nutzt den Windows-App-SDK-ResourceLoader
    /// (<c>Microsoft.Windows.ApplicationModel.Resources.ResourceLoader</c>), der ohne
    /// MSIX-Package-Identity funktioniert. Der WinRT-ResourceLoader
    /// (<c>Windows.ApplicationModel.Resources.ResourceLoader.GetForViewIndependentUse()</c>)
    /// braucht Package-Identity und crasht im unpackaged-Betrieb in KERNELBASE.dll — deshalb
    /// läuft der gesamte lokalisierte String-Zugriff der App über diesen Helper.
    /// In Unit-Tests (keine WinUI-Runtime / kein <c>resources.pri</c>) fällt jeder Aufruf auf
    /// den Fallback-Wert zurück.
    /// </remarks>
    internal static class SafeResourceLoader
    {
        // Der Windows-App-SDK-ResourceLoader lädt per Default 'resources.pri' aus dem
        // App-Verzeichnis (im Publish enthalten) und die 'Resources'-Map. Lazy erzeugt, damit
        // Test-Hosts ohne WinUI-Runtime nicht schon beim Typ-Init scheitern.
        private static Microsoft.Windows.ApplicationModel.Resources.ResourceLoader? _loader;

        /// <summary>
        /// Lädt den lokalisierten Wert für <paramref name="key"/>. Ist die WinUI-Runtime nicht
        /// verfügbar, tritt ein nativer Fehler auf oder fehlt der Schlüssel, wird
        /// <paramref name="fallback"/> zurückgegeben.
        /// </summary>
        /// <param name="key">Der Ressourcen-Schlüssel (z. B. <c>"UpdateAvailableTitle"</c>).</param>
        /// <param name="fallback">Rückfallwert für Tests oder fehlende Einträge.</param>
        /// <returns>Der lokalisierte String oder der Fallback.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Resource-Loader-Guard: 'Application.Current' und der ResourceLoader können im Test-Host oder bei fehlendem resources.pri eine native Exception werfen; der Fallback darf unabhängig vom konkreten Fehlertyp greifen.")]
        public static string Get(string key, string fallback = "")
        {
            try
            {
                // In Unit-Tests gibt es keine App-Instanz; Application.Current wirft dort teils
                // eine COMException.
                if (Application.Current is null)
                {
                    return fallback;
                }

                _loader ??= new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
                string value = _loader.GetString(key);
                return string.IsNullOrEmpty(value) ? fallback : value;
            }
            catch
            {
                return fallback;
            }
        }
    }

    /// <summary>
    /// Instanz-Fassade mit <c>GetString(key)</c> für Aufrufstellen, die bisher einen
    /// <c>ResourceLoader</c> in einem Feld/Local hielten. Delegiert an
    /// <see cref="SafeResourceLoader.Get(string, string)"/> und ist damit ebenfalls
    /// unpackaged-tauglich.
    /// </summary>
    internal sealed class SafeResourceStrings
    {
        /// <summary>Lädt den lokalisierten Wert für <paramref name="key"/> (leer, wenn nicht vorhanden).</summary>
        /// <param name="key">Der Ressourcen-Schlüssel.</param>
        /// <returns>Der lokalisierte String oder <see cref="string.Empty"/>.</returns>
        public string GetString(string key) => SafeResourceLoader.Get(key);
    }
}
