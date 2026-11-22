using EchoPlay.Core.Http;
using System.Diagnostics;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Öffnet URLs im Standard-Browser, aber ausschließlich mit <c>http</c>/<c>https</c>-Schema.
    /// <para>
    /// <see cref="Process.Start(ProcessStartInfo)"/> mit <c>UseShellExecute = true</c> startet
    /// bei beliebigem <c>FileName</c> das per Datei-/Protokoll-Assoziation zuständige Programm.
    /// Eine manipulierte URL (z.B. eine aus der Datenbank gelesene <c>ProviderUrl</c> mit
    /// <c>file://</c>, einem Pfad auf eine <c>.exe</c> oder einem Custom-Protocol) könnte so
    /// ungewollt Code ausführen. Dieser Guard lässt nur Web-Links durch.
    /// </para>
    /// </summary>
    internal static class SafeUrlLauncher
    {
        /// <summary>
        /// Öffnet <paramref name="url"/> im Standard-Browser, sofern es sich um eine
        /// absolute <c>http</c>/<c>https</c>-URL handelt.
        /// </summary>
        /// <param name="url">Die zu öffnende URL (darf null/leer sein).</param>
        /// <returns><see langword="true"/>, wenn der Browser gestartet wurde; sonst <see langword="false"/>.</returns>
        public static bool TryOpenInBrowser(string? url) => TryOpenInBrowser(WebAddress.TryParse(url));

        /// <summary>
        /// Öffnet <paramref name="url"/> im Standard-Browser, sofern es sich um eine
        /// absolute <c>http</c>/<c>https</c>-Adresse handelt.
        /// </summary>
        /// <param name="url">Die zu öffnende Adresse (darf null sein).</param>
        /// <returns><see langword="true"/>, wenn der Browser gestartet wurde; sonst <see langword="false"/>.</returns>
        public static bool TryOpenInBrowser(Uri? url)
        {
            // Auch eine bereits geprüfte Uri geht noch einmal durch die Regel: Diese Methode ist
            // die letzte Stelle vor der Shell und darf sich auf keinen Aufrufer verlassen.
            if (url is null || WebAddress.TryParse(url.OriginalString) is null)
            {
                return false;
            }

            return ShellLauncher.TryStart(url.AbsoluteUri, out _);
        }

        /// <summary>
        /// Öffnet eine Anwendungs-URI eines fest vorgegebenen Schemas, z.B. <c>spotify:album:…</c>.
        /// Damit landet der Nutzer in der installierten App, in der er angemeldet ist. Im
        /// Browser ist er das meist nicht.
        /// </summary>
        /// <param name="uri">Die zu öffnende URI.</param>
        /// <param name="expectedScheme">Das einzige erlaubte Schema, kleingeschrieben (z.B. <c>spotify</c>).</param>
        /// <returns>
        /// <see langword="true"/>, wenn die App gestartet wurde; <see langword="false"/>, wenn das
        /// Schema nicht passt oder kein Programm dafür registriert ist. Der Aufrufer weicht dann
        /// auf den Web-Link aus.
        /// </returns>
        /// <remarks>
        /// Bewusst getrennt von <see cref="TryOpenInBrowser(Uri)"/>: Dort bleibt es bei http/https, damit
        /// eine aus der Datenbank gelesene Provider-Angabe niemals ein beliebiges Programm startet.
        /// Hier baut ausschließlich EchoPlay selbst die URI aus geprüften Bestandteilen, und das
        /// Schema muss exakt dem erwarteten entsprechen.
        /// </remarks>
        public static bool TryOpenAppLink(string? uri, string expectedScheme)
        {
            if (string.IsNullOrWhiteSpace(uri) || string.IsNullOrWhiteSpace(expectedScheme))
            {
                return false;
            }

            if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed))
            {
                return false;
            }

            if (!string.Equals(parsed.Scheme, expectedScheme, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Ohne registrierten Handler meldet die Shell false; der Aufrufer nimmt dann den Web-Link.
            return ShellLauncher.TryStart(uri, out _);
        }
    }
}
