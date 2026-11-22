using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

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
        /// <param name="xamlRoot">XamlRoot der aufrufenden Page, Pflicht für WinUI 3 ContentDialog.</param>
        /// <param name="title">Titelzeile des Dialogs.</param>
        /// <param name="content">Anzuzeigender Text, wird in einem ScrollViewer mit selektierbarem TextBlock dargestellt.</param>
        /// <param name="primaryButtonText">Optionaler Primärbutton, etwa "Als TXT speichern"; <see langword="null"/> zeigt nur die Schließen-Schaltfläche.</param>
        /// <param name="closeButtonText">Beschriftung der Schließen-Schaltfläche; ohne Angabe die Ressource <c>CommonCloseButton</c>.</param>
        /// <param name="maxHeight">Maximale Höhe des ScrollViewers. Default: 400.</param>
        /// <param name="useMonospace">True schaltet die Mono-Schrift des Themes ein (sinnvoll für Berichte und Dateilisten).</param>
        /// <param name="monospaceFontSize">Schriftgröße bei Monospace-Modus (default 14, Berichte können kleiner laufen lassen).</param>
        /// <param name="defaultButton">Welcher Button durch Enter ausgelöst wird; wichtig bei destruktiven Aktionen.</param>
        public static async Task<ContentDialogResult> ShowAsync(
            XamlRoot xamlRoot,
            string title,
            string content,
            string? primaryButtonText = null,
            string? closeButtonText = null,
            double maxHeight = 400,
            bool useMonospace = false,
            double monospaceFontSize = 14,
            ContentDialogButton defaultButton = ContentDialogButton.None)
        {
            ContentDialog dialog = new()
            {
                XamlRoot = xamlRoot,
                Title = title,
                Content = new ScrollViewer
                {
                    MaxHeight = maxHeight,
                    Content = CreateTextBlock(content, useMonospace, monospaceFontSize)
                },
                CloseButtonText = closeButtonText ?? SafeResourceLoader.Get("CommonCloseButton", "OK"),
                DefaultButton = defaultButton
            };

            if (primaryButtonText is not null)
            {
                dialog.PrimaryButtonText = primaryButtonText;
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

        private static TextBlock CreateTextBlock(string content, bool useMonospace, double monospaceFontSize)
        {
            TextBlock textBlock = new()
            {
                Text = content,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            };

            if (useMonospace)
            {
                textBlock.FontFamily = ThemeFonts.Mono();
                textBlock.FontSize = monospaceFontSize;
                // Berichte mit fester Spaltenbreite sollen nicht umgebrochen werden
                textBlock.TextWrapping = TextWrapping.NoWrap;
            }

            return textBlock;
        }
    }
}
