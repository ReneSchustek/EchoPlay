using System;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Reine Fallback-Logik fuer Ressourcen-Lookup. Trennt die Entscheidung
    /// "was tun, wenn der Schluessel fehlt?" vom WinUI-spezifischen ResourceLoader,
    /// damit die Politik testbar bleibt.
    ///
    /// Default-Verhalten bei nicht gefundenem Key: Marker <c>"[!]{key}"</c>, damit
    /// fehlende Keys in der UI sofort sichtbar sind. Leere Keys liefern <see cref="string.Empty"/>.
    /// </summary>
    public static class ResourceKeyResolver
    {
        /// <summary>
        /// Marker-Praefix fuer fehlende Ressourcen-Keys.
        /// </summary>
        public const string MissingKeyMarker = "[!]";

        /// <summary>
        /// Loest einen Ressourcen-Lookup auf. Liefert das Ergebnis aus
        /// <paramref name="loader"/>, faellt bei leerer Antwort auf
        /// <c>"[!]{key}"</c> zurueck (sichtbar in der UI). Leere Keys
        /// liefern unmittelbar <see cref="string.Empty"/>.
        /// </summary>
        /// <param name="key">Ressourcen-Schluessel.</param>
        /// <param name="loader">Function, die einen Schluessel auf den ResourceLoader-Wert mappt.</param>
        public static string Resolve(string? key, Func<string, string> loader)
        {
            ArgumentNullException.ThrowIfNull(loader);

            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            string value = loader(key);
            if (string.IsNullOrEmpty(value))
            {
                return $"{MissingKeyMarker}{key}";
            }
            return value;
        }
    }
}
