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
    /// Zeigt Bestätigungs-Dialoge über den WinUI-3-ContentDialog an.
    /// Content-Aufbau (Texte + lokalisierte Buttons) liegt in
    /// <see cref="ConfirmationDialogContent.Build"/>, damit ohne XamlRoot testbar.
    /// </summary>
    public sealed class ConfirmationDialogService : IConfirmationDialogService
    {
        private readonly Func<XamlRoot?> _xamlRootProvider;
        private readonly IDialogSuppressionService _suppressionService;

        /// <summary>
        /// Standard-Konstruktor: nutzt <see cref="App.MainWindow"/> zur Laufzeit.
        /// </summary>
        /// <param name="suppressionService">Kennt die dauerhaft ausgeblendeten Rückfragen.</param>
        public ConfirmationDialogService(IDialogSuppressionService suppressionService)
            : this(static () => App.MainWindow?.Content?.XamlRoot, suppressionService)
        {
        }

        /// <summary>
        /// Test-Konstruktor: erlaubt das Einsetzen eines Fake-XamlRoot-Providers
        /// (auch null für Pre-MainWindow-Szenarien).
        /// </summary>
        /// <param name="xamlRootProvider">Liefert die Zeichenwurzel oder <see langword="null"/>.</param>
        /// <param name="suppressionService">Kennt die dauerhaft ausgeblendeten Rückfragen.</param>
        internal ConfirmationDialogService(Func<XamlRoot?> xamlRootProvider, IDialogSuppressionService suppressionService)
        {
            ArgumentNullException.ThrowIfNull(xamlRootProvider);
            ArgumentNullException.ThrowIfNull(suppressionService);

            _xamlRootProvider = xamlRootProvider;
            _suppressionService = suppressionService;
        }

        /// <inheritdoc />
        /// <param name="title">Titel des Dialogs.</param>
        /// <param name="message">Die Frage oder Erklärung für den Benutzer.</param>
        /// <param name="key">Kennung der Rückfrage.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        public async Task<bool> ConfirmAsync(string title, string message, DialogKey key, CancellationToken cancellationToken = default)
        {
            // Dauerhaft ausgeblendet heißt: Der Nutzer hat diese Frage einmal mit „Ja"
            // beantwortet und um Ruhe gebeten. Nur „Ja" wird gemerkt, deshalb ist die
            // gemerkte Antwort immer die zustimmende.
            if (await _suppressionService.IsSuppressedAsync(key, cancellationToken))
            {
                return true;
            }

            ConfirmationDialogContent content = ConfirmationDialogContent.Build(title, message);

            // Defense-in-Depth analog ErrorDialogService: vor abgeschlossener
            // MainWindow-Init ist MainWindow oder XamlRoot null. Die Antwort ist dann
            // "nicht bestätigt" — eine Rückfrage, die niemand sehen konnte, darf keine
            // Zustimmung sein, und alle Aufrufer brechen bei false ab.
            XamlRoot? xamlRoot = _xamlRootProvider();
            if (xamlRoot is null)
            {
                EmergencyTrace.Log($"ConfirmationDialogService: {content.Title} — {content.Message} (MainWindow nicht verfügbar, als abgelehnt behandelt)");
                return false;
            }

            SuppressibleDialogContent body = SuppressibleDialogContent.ForConfirmation(content.Message);

            ContentDialog dialog = new()
            {
                Title = content.Title,
                Content = body.Root,
                PrimaryButtonText = content.PrimaryButtonText,
                CloseButtonText = content.CloseButtonText,
                XamlRoot = xamlRoot
            };

            ContentDialogDragHelper.MakeDraggable(dialog);
            ContentDialogResult result = await dialog.ShowAsync();
            bool confirmed = result == ContentDialogResult.Primary;

            if (DialogSuppressionDecision.ShouldRemember(key, body.IsSuppressRequested, confirmed))
            {
                await _suppressionService.SuppressAsync(key, cancellationToken);
            }

            return confirmed;
        }
    }
}
