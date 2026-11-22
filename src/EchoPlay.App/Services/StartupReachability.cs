using EchoPlay.Core.Models;
using EchoPlay.Data.Entities.Settings;
using EchoPlay.Logger.Abstractions;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Prüft beim Start, ob Anbieter und lokale Mediathek erreichbar sind. Ein Ergebnis
    /// „nicht erreichbar“ trägt den Schlüssel des Hinweises, den die Startseite zeigt.
    /// </summary>
    /// <param name="httpClientFactory">Fabrik für den Client „OnlineCheck“.</param>
    /// <param name="logger">Protokoll des Startlaufs.</param>
    internal sealed class StartupReachability(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        /// <summary>
        /// Prüft die Erreichbarkeit der Anbieter. Ohne Anbieter oder im Offline-Modus entfällt
        /// die Prüfung, dann gilt „nicht online“ ohne Hinweis, weil es kein Fehler ist.
        /// </summary>
        /// <param name="settings">Die gespeicherten Einstellungen.</param>
        /// <param name="onStatus">Nimmt die Statuszeile des Startbilds an.</param>
        /// <param name="cancellationToken">Abbruch des Startlaufs.</param>
        /// <returns>Ob online gearbeitet werden kann, und sonst der Schlüssel des Hinweises.</returns>
        public async Task<(bool IsAvailable, string? Hint)> CheckOnlineAsync(
            AppSettings settings, Action<string>? onStatus, CancellationToken cancellationToken)
        {
            if (settings.OfflineMode)
            {
                return (false, null);
            }

            if (settings.ActiveProvider == ProviderType.None)
            {
                return (true, null);
            }

            onStatus?.Invoke(SplashStatus.Text("SplashStatusCheckingConnection", "Prüfe Internetverbindung …"));
            bool isAvailable = await CheckConnectivityAsync(cancellationToken);

            if (isAvailable)
            {
                return (true, null);
            }

            logger.Warning("Online-Konnektivitätscheck fehlgeschlagen – Offline-Modus temporär aktiv.");
            return (false, "StartupOnlineUnavailableHint");
        }

        /// <summary>Prüft die Erreichbarkeit des lokalen Bibliotheksordners.</summary>
        /// <param name="settings">Die gespeicherten Einstellungen.</param>
        /// <param name="onStatus">Nimmt die Statuszeile des Startbilds an.</param>
        /// <returns>Ob der Ordner lesbar ist, und sonst der Schlüssel des Hinweises.</returns>
        public (bool IsAvailable, string? Hint) CheckLocal(AppSettings settings, Action<string>? onStatus)
        {
            if (!settings.LocalLibraryEnabled || string.IsNullOrWhiteSpace(settings.LocalLibraryRootPath))
            {
                return (true, null);
            }

            onStatus?.Invoke(SplashStatus.Text("SplashStatusCheckingLocalLibrary", "Prüfe lokale Bibliothek …"));

            if (CanReadDirectory(settings.LocalLibraryRootPath))
            {
                return (true, null);
            }

            logger.Warning("Lokales Verzeichnis nicht erreichbar: {RootPath}", settings.LocalLibraryRootPath);
            return (false, "StartupLocalLibraryUnavailableHint");
        }

        /// <summary>
        /// Prüft die Online-Konnektivität per HTTP-HEAD-Request auf die iTunes-API.
        /// Leichtgewichtig: kein Body, nur Verbindungsaufbau und Antwort.
        /// </summary>
        private async Task<bool> CheckConnectivityAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Der Named-Client „OnlineCheck" hat ein kurzes Timeout (5 s) und einen eigenen
                // User-Agent. Über IHttpClientFactory werden die Handler wiederverwendet; ein
                // eigener HttpClient je Prüfung erschöpfte auf Dauer die Sockets.
                HttpClient client = httpClientFactory.CreateClient("OnlineCheck");
                using HttpRequestMessage request = new(HttpMethod.Head, "https://itunes.apple.com/search?term=test&limit=1");
                using HttpResponseMessage response = await client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) when (ex is HttpRequestException
                || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // Zeitgrenze oder keine Verbindung: offline. Ein Abbruch des Aufrufers geht nach oben.
                logger.Debug(() => $"Online-Check fehlgeschlagen: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Prüft, ob das Verzeichnis existiert und lesbar ist. Ein reiner
        /// <see cref="Directory.Exists"/>-Check reicht nicht, denn das Verzeichnis könnte
        /// existieren, aber nicht lesbar sein (z.B. Netzlaufwerk ohne Verbindung).
        /// </summary>
        private bool CanReadDirectory(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return false;
                }

                // Lesezugriff testen (löst IOException bei nicht erreichbaren Netzlaufwerken aus)
                _ = Directory.GetDirectories(path);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.Debug(() => $"Lokales Verzeichnis nicht lesbar: {ex.Message}");
                return false;
            }
        }
    }
}
