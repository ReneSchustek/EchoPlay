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
    }
}
