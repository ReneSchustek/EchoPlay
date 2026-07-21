using EchoPlay.Core.Models;
using EchoPlay.Logger.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Manuell ausgelöste Update-Prüfung (z. B. über einen „Auf Updates prüfen"-Button auf der
    /// Über-Seite). Ergänzt die automatische Start-Prüfung um einen On-Demand-Pfad: Bei einem
    /// verfügbaren Update erscheint der Update-Dialog auf dem übergebenen <see cref="XamlRoot"/>
    /// des Hauptfensters, andernfalls eine „bereits aktuell"-Meldung.
    /// </summary>
    public sealed class UpdateInteractionService
    {
        private readonly UpdateCheckService _checkService;
        private readonly UpdateDownloadService _downloadService;
        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert den Service mit den Update-Diensten.
        /// </summary>
        /// <param name="checkService">Prüft auf neue Versionen.</param>
        /// <param name="downloadService">Lädt die Setup-Datei und startet den Installer.</param>
        /// <param name="loggerFactory">Logger-Fabrik.</param>
        public UpdateInteractionService(
            UpdateCheckService checkService,
            UpdateDownloadService downloadService,
            ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _checkService = checkService;
            _downloadService = downloadService;
            _logger = loggerFactory.CreateLogger(nameof(UpdateInteractionService));
        }

        /// <summary>
        /// Prüft on-demand auf ein Update und führt den Nutzer durch das Ergebnis: Update-Dialog
        /// bei verfügbarer Version, sonst „bereits aktuell". Fehler werden abgefangen und als
        /// Hinweis angezeigt — der Aufruf wirft nie.
        /// </summary>
        /// <param name="xamlRoot">Der <see cref="XamlRoot"/> des Hauptfensters für die Dialoge.</param>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Manuelle Update-Prüfung: HTTP-, JSON- oder Dialog-Fehler dürfen die App nicht stören; der Nutzer bekommt stattdessen einen Fehlerhinweis.")]
        public async Task CheckForUpdatesAsync(XamlRoot xamlRoot)
        {
            ArgumentNullException.ThrowIfNull(xamlRoot);

            try
            {
                UpdateInfo? update = await _checkService.CheckForUpdateAsync();

                if (update is null)
                {
                    await ShowMessageAsync(xamlRoot, "UpdateUpToDateTitle", "UpdateUpToDateMessage");
                    return;
                }

                await PromptAndInstallAsync(update, xamlRoot);
            }
            catch (Exception ex)
            {
                _logger.Warning("Manuelle Update-Prüfung fehlgeschlagen: {Reason}", ex.Message);
                await ShowMessageAsync(xamlRoot, "UpdateCheckFailedTitle", "UpdateCheckFailedMessage");
            }
        }

        /// <summary>
        /// Zeigt den Drei-Optionen-Dialog (Jetzt / Später / Überspringen) und wickelt bei
        /// „Jetzt" Download, Hash-Verifikation und Installer-Start ab.
        /// </summary>
        private async Task PromptAndInstallAsync(UpdateInfo update, XamlRoot xamlRoot)
        {
            _logger.Info("Neue Version verfügbar (manuelle Prüfung): {Version}", update.Version);

            System.Text.CompositeFormat messageFormat = System.Text.CompositeFormat.Parse(
                EchoPlay.App.Helpers.SafeResourceLoader.Get("UpdateAvailableMessage"));
            string content = string.Format(System.Globalization.CultureInfo.CurrentCulture, messageFormat, update.Version)
                + (string.IsNullOrWhiteSpace(update.ReleaseNotes)
                    ? string.Empty
                    : "\n\n" + update.ReleaseNotes);

            ContentDialog dialog = new()
            {
                Title = EchoPlay.App.Helpers.SafeResourceLoader.Get("UpdateAvailableTitle"),
                Content = content,
                PrimaryButtonText = EchoPlay.App.Helpers.SafeResourceLoader.Get("UpdateNowButton"),
                SecondaryButtonText = EchoPlay.App.Helpers.SafeResourceLoader.Get("UpdateLaterButton"),
                CloseButtonText = EchoPlay.App.Helpers.SafeResourceLoader.Get("UpdateSkipButton"),
                XamlRoot = xamlRoot,
                DefaultButton = ContentDialogButton.Primary
            };

            ContentDialogResult result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                ContentDialog progressDialog = new()
                {
                    Title = EchoPlay.App.Helpers.SafeResourceLoader.Get("UpdateAvailableTitle"),
                    Content = new StackPanel
                    {
                        Spacing = 16,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = EchoPlay.App.Helpers.SafeResourceLoader.Get("UpdateDownloadingMessage"),
                                TextWrapping = TextWrapping.Wrap
                            },
                            new ProgressBar { IsIndeterminate = true }
                        }
                    },
                    XamlRoot = xamlRoot
                };
                _ = progressDialog.ShowAsync();

                bool success = await _downloadService.DownloadAndInstallAsync(
                    update.DownloadUrl,
                    update.Version,
                    update.FileSizeBytes,
                    update.ExpectedSha256);

                progressDialog.Hide();

                if (success)
                {
                    // Installer gestartet → App beenden, damit er die Dateien ersetzen kann.
                    Application.Current.Exit();
                    return;
                }

                await ShowMessageAsync(xamlRoot, "UpdateDownloadFailedTitle", "UpdateDownloadFailedMessage");
            }
            else if (result == ContentDialogResult.None)
            {
                await _checkService.SkipVersionAsync(update.Version);
                _logger.Info("Version {Version} übersprungen (manuelle Prüfung)", update.Version);
            }
        }

        /// <summary>Zeigt einen einfachen Hinweis-Dialog mit OK-Schaltfläche.</summary>
        private static async Task ShowMessageAsync(XamlRoot xamlRoot, string titleKey, string messageKey)
        {
            ContentDialog dialog = new()
            {
                Title = EchoPlay.App.Helpers.SafeResourceLoader.Get(titleKey),
                Content = EchoPlay.App.Helpers.SafeResourceLoader.Get(messageKey),
                CloseButtonText = EchoPlay.App.Helpers.SafeResourceLoader.Get("CommonCloseButton"),
                XamlRoot = xamlRoot
            };
            _ = await dialog.ShowAsync();
        }
    }
}
