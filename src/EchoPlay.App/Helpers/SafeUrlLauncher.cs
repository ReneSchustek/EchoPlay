using System;
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
        public static bool TryOpenInBrowser(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                return false;
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                return false;
            }

            _ = Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });

            return true;
        }

        /// <summary>
        /// Öffnet eine Anwendungs-URI eines fest vorgegebenen Schemas, z.B. <c>spotify:album:…</c>.
        /// Damit landet der Nutzer in der installierten App, in der er angemeldet ist — im
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
        /// Bewusst getrennt von <see cref="TryOpenInBrowser"/>: Dort bleibt es bei http/https, damit
        /// eine aus der Datenbank gelesene Provider-Angabe niemals ein beliebiges Programm startet.
        /// Hier baut ausschließlich EchoPlay selbst die URI aus geprüften Bestandteilen, und das
        /// Schema muss exakt dem erwarteten entsprechen.
        /// </remarks>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Protokoll-Start ist ein Zusatzangebot: Ist die App nicht installiert oder das Protokoll nicht registriert, wirft ShellExecute (Win32Exception) oder das Handler-Programm selbst. Der Aufrufer weicht dann auf den Web-Link aus – ein Absturz wäre die schlechtere Antwort.")]
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

            try
            {
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = uri,
                    UseShellExecute = true
                });

                return true;
            }
            catch (Exception)
            {
                // Kein Handler registriert – der Aufrufer nimmt den Web-Link.
                return false;
            }
        }
    }
}
