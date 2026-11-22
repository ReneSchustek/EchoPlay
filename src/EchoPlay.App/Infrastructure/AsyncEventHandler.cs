using EchoPlay.App.Helpers;
using EchoPlay.App.Services;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace EchoPlay.App.Infrastructure
{
    /// <summary>
    /// Sicherer Wrapper für <c>async void</c>-Event-Handler in WinUI-3-Pages und
    /// Windows. Fängt alle Exceptions ab, loggt mit Operationsnamen und zeigt einen
    /// Fehlerdialog. <see cref="OperationCanceledException"/> wird stillschweigend
    /// geschluckt, denn beabsichtigte Abbrüche sind keine Fehler.
    /// </summary>
    internal static class AsyncEventHandler
    {
        /// <summary>
        /// Bequemer Overload, der Dialog- und Logger-Service aus dem globalen
        /// <see cref="App.Services"/>-Provider holt. Sinnvoll in Page-Code-Behinds,
        /// die keine eigenen Felder dafür halten wollen.
        /// </summary>
        /// <param name="action">Die auszuführende Aktion.</param>
        /// <param name="operationName">
        /// Name der Operation (per <see cref="CallerMemberNameAttribute"/> automatisch
        /// gesetzt, wenn weggelassen).
        /// </param>
        public static Task RunSafelyAsync(
            Func<Task> action,
            [CallerMemberName] string operationName = "")
        {
            IErrorDialogService errorDialog =
                App.Services.GetRequiredService<IErrorDialogService>();
            ILoggerFactory loggerFactory =
                App.Services.GetRequiredService<ILoggerFactory>();
            ILogger logger = loggerFactory.CreateLogger("PageHandler");
            return RunSafelyAsync(action, errorDialog, logger, operationName);
        }

        /// <summary>
        /// Führt eine asynchrone Aktion aus und fängt alle Exceptions ab. Muss aus
        /// einem <c>async void</c>-Event-Handler mit <c>await</c> gerufen werden.
        /// </summary>
        /// <param name="action">Die auszuführende Aktion.</param>
        /// <param name="errorDialog">Dialog-Service für die Nutzer-Rückmeldung.</param>
        /// <param name="logger">Logger, in dem der Fehler mit Operationsnamen landet.</param>
        /// <param name="operationName">
        /// Name der Operation (per <see cref="CallerMemberNameAttribute"/> automatisch
        /// gesetzt, wenn weggelassen).
        /// </param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Sicherheitsnetz für async-void-UI-Handler: jede Exception wird geloggt und als Dialog angezeigt, damit die App nicht abstürzt.")]
        public static async Task RunSafelyAsync(
            Func<Task> action,
            IErrorDialogService errorDialog,
            ILogger logger,
            [CallerMemberName] string operationName = "")
        {
            try
            {
                await action().ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // Beabsichtigter Abbruch: keine Meldung, kein Log
            }
            catch (Exception ex)
            {
                logger.Warning("Handler '{OperationName}' fehlgeschlagen: {Reason}", operationName, ex.Message);
                try
                {
                    // Ohne geladene Ressourcen bleibt wenigstens die Fehlermeldung selbst stehen.
                    string messageFormat = SafeResourceLoader.Get("UnexpectedErrorMessage", "{0}");
                    await errorDialog.ShowAlwaysAsync(
                        SafeResourceLoader.Get("UnexpectedErrorTitle"),
                        string.Format(CultureInfo.CurrentCulture, messageFormat, ex.Message))
                        .ConfigureAwait(true);
                }
                catch
                {
                    // WinUI erlaubt nur einen offenen Dialog; ist schon einer offen, bleibt es beim
                    // Protokolleintrag oben.
                }
            }
        }
    }
}
