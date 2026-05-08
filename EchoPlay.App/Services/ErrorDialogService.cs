using EchoPlay.App.Helpers;
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
        /// <inheritdoc />
        public async Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            ErrorDialogContent content = ErrorDialogContent.Build(title, message);

            ContentDialog dialog = new()
            {
                Title = content.Title,
                Content = content.Message,
                CloseButtonText = content.CloseButtonText,
                XamlRoot = App.MainWindow!.Content.XamlRoot
            };

            ContentDialogDragHelper.MakeDraggable(dialog);
            _ = await dialog.ShowAsync();
        }
    }
}
