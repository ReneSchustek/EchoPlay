using EchoPlay.App.Helpers;
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

        /// <summary>
        /// Standard-Konstruktor: nutzt <see cref="App.MainWindow"/> zur Laufzeit.
        /// </summary>
        public ConfirmationDialogService()
            : this(static () => App.MainWindow?.Content?.XamlRoot)
        {
        }

        /// <summary>
        /// Test-Konstruktor: erlaubt das Einsetzen eines Fake-XamlRoot-Providers
        /// (auch null für Pre-MainWindow-Szenarien).
        /// </summary>
        internal ConfirmationDialogService(Func<XamlRoot?> xamlRootProvider)
        {
            ArgumentNullException.ThrowIfNull(xamlRootProvider);

            _xamlRootProvider = xamlRootProvider;
        }

        /// <inheritdoc />
        public async Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default)
        {
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

            ContentDialog dialog = new()
            {
                Title = content.Title,
                Content = content.Message,
                PrimaryButtonText = content.PrimaryButtonText,
                CloseButtonText = content.CloseButtonText,
                XamlRoot = xamlRoot
            };

            ContentDialogDragHelper.MakeDraggable(dialog);
            ContentDialogResult result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }
    }
}
