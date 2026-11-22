using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Wiederverwendbarer ContentDialog mit ScrollViewer und auswählbarem TextBlock.
    /// Wird überall dort eingesetzt, wo ein längerer, schreibgeschützter Text mit
    /// optionalem Aktions-Button (etwa "Als TXT speichern" oder "Umbauen") angezeigt werden soll.
    /// Kapselt Drag-Helper-Verdrahtung, COM-Exception-Behandlung und einheitliches Styling.
    /// </summary>
    internal static class ScrollableTextDialog
    {
        /// <summary>
        /// Zeigt den Dialog modal an und gibt das Resultat zurück.
        /// Bei einer <see cref="COMException"/>, etwa weil bereits ein anderer Dialog offen ist,
        /// liefert die Methode <see cref="ContentDialogResult.None"/> zurück und wirft nicht weiter.
        /// </summary>
        /// <param name="options">Schaltflächen, Höhe und Schrift; ohne Angabe die Vorgaben.</param>
        /// <param name="xamlRoot">XamlRoot der aufrufenden Page, Pflicht für WinUI 3 ContentDialog.</param>
        /// <param name="title">Titelzeile des Dialogs.</param>
        /// <param name="content">Anzuzeigender Text, wird in einem ScrollViewer mit selektierbarem TextBlock dargestellt.</param>
        public static async Task<ContentDialogResult> ShowAsync(
            XamlRoot xamlRoot,
            string title,
            string content,
            ScrollableTextDialogOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            ContentDialog dialog = new()
            {
                XamlRoot = xamlRoot,
                Title = title,
                Content = new ScrollViewer
                {
                    MaxHeight = options.MaxHeight,
                    Content = CreateTextBlock(content, options)
                },
                CloseButtonText = options.CloseButtonText ?? SafeResourceLoader.Get("CommonCloseButton", "OK"),
                DefaultButton = options.DefaultButton
            };

            if (options.PrimaryButtonText is not null)
            {
                dialog.PrimaryButtonText = options.PrimaryButtonText;
            }

            try
            {
                ContentDialogDragHelper.MakeDraggable(dialog);
                return await dialog.ShowAsync();
            }
            catch (COMException)
            {
                // Ein anderer ContentDialog ist bereits offen. Der Aufrufer entscheidet,
                // wie damit umgegangen wird. None signalisiert "Dialog wurde nicht gezeigt".
                return ContentDialogResult.None;
            }
        }

        private static TextBlock CreateTextBlock(string content, ScrollableTextDialogOptions options)
        {
            TextBlock textBlock = new()
            {
                Text = content,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            };

            if (options.UseMonospace)
            {
                textBlock.FontFamily = ThemeFonts.Mono();
                textBlock.FontSize = options.MonospaceFontSize;
                // Berichte mit fester Spaltenbreite sollen nicht umgebrochen werden
                textBlock.TextWrapping = TextWrapping.NoWrap;
            }

            return textBlock;
        }
    }
}
