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
    /// Führt den Nutzer durch eine verfügbare Aktualisierung. Der Dienst bedient beide
    /// Wege zum selben Dialog: die Prüfung beim Start, die im Startbild läuft, und die
    /// Prüfung auf Knopfdruck von der Über-Seite. Beide teilen sich Dialog, Download und
    /// Installationsstart; sie unterscheiden sich nur darin, wie der Fortschritt gezeigt
    /// wird — im Startbild als Statuszeile, sonst als eigener Fortschrittsdialog.
    /// </summary>
    public sealed class UpdateInteractionService
    {
        private readonly UpdateCheckService _checkService;
        private readonly UpdateDownloadService _downloadService;
        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert den Dienst mit den Aktualisierungs-Diensten.
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
        /// Prüft auf Knopfdruck auf eine Aktualisierung und führt den Nutzer durch das
        /// Ergebnis: Aktualisierungs-Dialog bei verfügbarer Version, sonst der Hinweis
        /// „bereits aktuell". Fehler werden abgefangen und als Hinweis angezeigt — der
        /// Aufruf wirft nie.
        /// </summary>
        /// <param name="xamlRoot">Die Zeichenwurzel des Hauptfensters für die Dialoge.</param>
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

                _logger.Info("Neue Version verfügbar (manuelle Prüfung): {Version}", update.Version);
                await PromptAndInstallAsync(update, xamlRoot, new ProgressDialogFeedback(xamlRoot));
            }
            catch (Exception ex)
            {
                _logger.Warning("Manuelle Update-Prüfung fehlgeschlagen: {Reason}", ex.Message);
                await ShowMessageAsync(xamlRoot, "UpdateCheckFailedTitle", "UpdateCheckFailedMessage");
            }
        }

        /// <summary>
        /// Prüft beim Start auf eine Aktualisierung. Läuft im Startbild, bevor das
        /// Hauptfenster erscheint; der Fortschritt geht deshalb in dessen Statuszeile.
        /// Ist keine neuere Version da, geschieht nichts. Fehler werden protokolliert und
        /// verschluckt — der Start darf daran nicht scheitern.
        /// </summary>
        /// <param name="xamlRoot">Die Zeichenwurzel des Startbilds für die Dialoge.</param>
        /// <param name="reportStatus">Nimmt die Statuszeile des Startbilds entgegen.</param>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Update-Check ist nicht kritischer Pfad: GitHub-HTTP-Fehler, JSON-Parse-Fehler oder Dialog-Probleme dürfen den App-Start nicht blockieren.")]
        public async Task PromptOnStartupAsync(XamlRoot xamlRoot, Action<string> reportStatus)
        {
            ArgumentNullException.ThrowIfNull(xamlRoot);
            ArgumentNullException.ThrowIfNull(reportStatus);

            try
            {
                UpdateInfo? update = await _checkService.CheckForUpdateAsync();

                if (update is null)
                {
                    return;
                }

                _logger.Info("Neue Version verfügbar: {Version}", update.Version);
                await PromptAndInstallAsync(update, xamlRoot, new StatusTextFeedback(reportStatus));
            }
            catch (Exception ex)
            {
                _logger.Warning("Update-Check fehlgeschlagen: {Reason}", ex.Message);
            }
        }

        /// <summary>
        /// Zeigt den Drei-Wege-Dialog (Jetzt / Später / Überspringen) und wickelt bei
        /// „Jetzt" Download, Prüfsummen-Vergleich und Installationsstart ab.
        /// </summary>
        /// <param name="update">Die gefundene neuere Version.</param>
        /// <param name="xamlRoot">Die Zeichenwurzel für die Dialoge.</param>
        /// <param name="feedback">Zeigt den Fortschritt während des Downloads an.</param>
        private async Task PromptAndInstallAsync(UpdateInfo update, XamlRoot xamlRoot, IDownloadFeedback feedback)
        {
            ContentDialogResult result = await BuildOfferDialog(update, xamlRoot).ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                await DownloadAndInstallAsync(update, xamlRoot, feedback);
            }
            else if (result == ContentDialogResult.None)
            {
                // „Version überspringen" — in der Datenbank merken, damit sie beim nächsten
                // Start nicht erneut angeboten wird.
                await _checkService.SkipVersionAsync(update.Version);
                _logger.Info("Version {Version} übersprungen", update.Version);
            }
        }

        /// <summary>
        /// Lädt das Installationspaket, prüft die Prüfsumme und startet den Installer.
        /// Bei Erfolg beendet sich die Anwendung, damit der Installer die Dateien
        /// ersetzen kann; sonst bleibt ein Hinweis und der normale Ablauf geht weiter.
        /// </summary>
        private async Task DownloadAndInstallAsync(UpdateInfo update, XamlRoot xamlRoot, IDownloadFeedback feedback)
        {
            feedback.Begin();
            bool success;
            try
            {
                success = await _downloadService.DownloadAndInstallAsync(
                    update.DownloadUrl,
                    update.Version,
                    update.FileSizeBytes,
                    update.ExpectedSha256);
            }
            finally
            {
                feedback.End();
            }

            if (success)
            {
                Application.Current.Exit();
                return;
            }

            await ShowMessageAsync(xamlRoot, "UpdateDownloadFailedTitle", "UpdateDownloadFailedMessage");
        }

        /// <summary>Baut den Dialog, der die neue Version samt Änderungsliste anbietet.</summary>
        private static ContentDialog BuildOfferDialog(UpdateInfo update, XamlRoot xamlRoot)
        {
            System.Text.CompositeFormat messageFormat = System.Text.CompositeFormat.Parse(
                EchoPlay.App.Helpers.SafeResourceLoader.Get("UpdateAvailableMessage"));
            string content = string.Format(System.Globalization.CultureInfo.CurrentCulture, messageFormat, update.Version)
                + (string.IsNullOrWhiteSpace(update.ReleaseNotes)
                    ? string.Empty
                    : "\n\n" + update.ReleaseNotes);

            return new ContentDialog
            {
                Title = EchoPlay.App.Helpers.SafeResourceLoader.Get("UpdateAvailableTitle"),
                Content = content,
                PrimaryButtonText = EchoPlay.App.Helpers.SafeResourceLoader.Get("UpdateNowButton"),
                SecondaryButtonText = EchoPlay.App.Helpers.SafeResourceLoader.Get("UpdateLaterButton"),
                CloseButtonText = EchoPlay.App.Helpers.SafeResourceLoader.Get("UpdateSkipButton"),
                XamlRoot = xamlRoot,
                DefaultButton = ContentDialogButton.Primary
            };
        }

        /// <summary>Zeigt einen einfachen Hinweis-Dialog mit Schließen-Schaltfläche.</summary>
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

        /// <summary>
        /// Zeigt an, dass der Download läuft. Die beiden Wege in den Dialog unterscheiden
        /// sich nur hierin: Im Startbild gibt es noch kein Fenster für einen zweiten
        /// Dialog, dafür aber eine Statuszeile.
        /// </summary>
        private interface IDownloadFeedback
        {
            /// <summary>Der Download beginnt.</summary>
            void Begin();

            /// <summary>Der Download ist beendet — gleich ob erfolgreich oder nicht.</summary>
            void End();
        }

        /// <summary>Fortschrittsdialog über dem Hauptfenster.</summary>
        private sealed class ProgressDialogFeedback(XamlRoot xamlRoot) : IDownloadFeedback
        {
            private ContentDialog? _dialog;

            public void Begin()
            {
                _dialog = new ContentDialog
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

                // Bewusst nicht abgewartet: Der Dialog bleibt offen, bis der Download fertig
                // ist und End() ihn schließt.
                _ = _dialog.ShowAsync();
            }

            public void End() => _dialog?.Hide();
        }

        /// <summary>
        /// Statuszeile des Startbilds. Ein zweiter Dialog käme dort nicht in Frage — WinUI
        /// erlaubt je Zeichenwurzel nur einen offenen Dialog, und der Angebots-Dialog ist
        /// zu diesem Zeitpunkt gerade erst geschlossen.
        /// </summary>
        private sealed class StatusTextFeedback(Action<string> reportStatus) : IDownloadFeedback
        {
            public void Begin()
                => reportStatus(EchoPlay.App.Helpers.SafeResourceLoader.Get("UpdateDownloadingMessage"));

            public void End()
            {
                // Die Statuszeile wird vom weiteren Startlauf ohnehin überschrieben.
            }
        }
    }
}
