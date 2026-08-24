using EchoPlay.App.Helpers;
using EchoPlay.Core.Models;
using EchoPlay.Logger.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Zeigt Fehler-Dialoge über den WinUI-3-ContentDialog an.
    /// Content-Aufbau liegt in <see cref="ErrorDialogContent.Build"/> — testbar ohne XamlRoot.
    /// </summary>
    public sealed class ErrorDialogService : IErrorDialogService
    {
        private readonly Func<XamlRoot?> _xamlRootProvider;
        private readonly IDialogSuppressionService _suppressionService;

        /// <summary>
        /// Standard-Konstruktor: nutzt <see cref="App.MainWindow"/> zur Laufzeit.
        /// </summary>
        /// <param name="suppressionService">Kennt die dauerhaft ausgeblendeten Hinweise.</param>
        public ErrorDialogService(IDialogSuppressionService suppressionService)
            : this(static () => App.MainWindow?.Content?.XamlRoot, suppressionService)
        {
        }

        /// <summary>
        /// Test-Konstruktor: erlaubt das Einsetzen eines Fake-XamlRoot-Providers
        /// (auch null für Pre-MainWindow-Szenarien).
        /// </summary>
        /// <param name="xamlRootProvider">Liefert die Zeichenwurzel oder <see langword="null"/>.</param>
        /// <param name="suppressionService">Kennt die dauerhaft ausgeblendeten Hinweise.</param>
        internal ErrorDialogService(Func<XamlRoot?> xamlRootProvider, IDialogSuppressionService suppressionService)
        {
            ArgumentNullException.ThrowIfNull(xamlRootProvider);
            ArgumentNullException.ThrowIfNull(suppressionService);

            _xamlRootProvider = xamlRootProvider;
            _suppressionService = suppressionService;
        }

        /// <inheritdoc />
        /// <param name="title">Titel des Dialogs.</param>
        /// <param name="message">Die Meldung für den Benutzer.</param>
        /// <param name="key">Kennung des Hinweises.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task ShowAsync(string title, string message, DialogKey key, CancellationToken cancellationToken = default)
        {
            if (await _suppressionService.IsSuppressedAsync(key, cancellationToken))
            {
                // Ausgeblendet heißt ungesehen, nicht ungeschehen — das Protokoll behält den Vorgang.
                EmergencyTrace.Log($"ErrorDialogService: {title} — {message} (vom Nutzer dauerhaft ausgeblendet)");
                return;
            }

            await ShowCoreAsync(title, message, key);
        }

        /// <inheritdoc />
        /// <param name="title">Titel des Dialogs.</param>
        /// <param name="message">Die Meldung für den Benutzer.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public Task ShowAlwaysAsync(string title, string message, CancellationToken cancellationToken = default) =>
            ShowCoreAsync(title, message, DialogKey.None);

        /// <summary>
        /// Zeigt den Dialog. Mit <see cref="DialogKey.None"/> entfällt das Häkchen — dann gibt
        /// es nichts zu merken, und ein Kästchen ohne Wirkung wäre eine Lüge.
        /// </summary>
        /// <param name="title">Titel des Dialogs.</param>
        /// <param name="message">Die Meldung für den Benutzer.</param>
        /// <param name="key">Kennung des Hinweises oder <see cref="DialogKey.None"/>.</param>
        private async Task ShowCoreAsync(string title, string message, DialogKey key)
        {
            ErrorDialogContent content = ErrorDialogContent.Build(title, message);

            // Defense-in-Depth: bei Startup-Failures vor abgeschlossener MainWindow-Init
            // (siehe App.xaml.cs Fatal-Pfade) ist MainWindow oder XamlRoot null. Statt
            // NullReferenceException im Error-Service EmergencyTrace-Fallback, analog
            // SplashWindow-Pfad — der reguläre Logger ist hier ggf. selbst noch nicht da.
            XamlRoot? xamlRoot = _xamlRootProvider();
            if (xamlRoot is null)
            {
                EmergencyTrace.Log($"ErrorDialogService: {content.Title} — {content.Message} (MainWindow nicht verfügbar)");
                return;
            }

            SuppressibleDialogContent? body = key == DialogKey.None ? null : SuppressibleDialogContent.ForHint(content.Message);

            ContentDialog dialog = new()
            {
                Title = content.Title,
                Content = body is null ? content.Message : body.Root,
                CloseButtonText = content.CloseButtonText,
                XamlRoot = xamlRoot
            };

            ContentDialogDragHelper.MakeDraggable(dialog);
            _ = await dialog.ShowAsync();

            if (body is not null && DialogSuppressionDecision.ShouldRemember(key, body.IsSuppressRequested, true))
            {
                await _suppressionService.SuppressAsync(key);
            }
        }
    }
}
