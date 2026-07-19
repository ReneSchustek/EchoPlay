using EchoPlay.App.Helpers;
using Microsoft.UI.Xaml.Controls;
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
        /// <inheritdoc />
        public async Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            ConfirmationDialogContent content = ConfirmationDialogContent.Build(title, message);

            ContentDialog dialog = new()
            {
                Title = content.Title,
                Content = content.Message,
                PrimaryButtonText = content.PrimaryButtonText,
                CloseButtonText = content.CloseButtonText,
                XamlRoot = App.MainWindow!.Content.XamlRoot
            };

            ContentDialogDragHelper.MakeDraggable(dialog);
            ContentDialogResult result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }
    }
}
