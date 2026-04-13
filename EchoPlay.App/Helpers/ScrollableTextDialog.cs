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
    /// optionalem Aktions-Button (z.B. "Als TXT speichern", "Umbauen") angezeigt werden soll.
    /// Kapselt Drag-Helper-Verdrahtung, COM-Exception-Behandlung und einheitliches Styling.
    /// </summary>
    internal static class ScrollableTextDialog
    {
        /// <summary>
        /// Zeigt den Dialog modal an und gibt das Resultat zurück.
        /// Bei einer <see cref="COMException"/> (z.B. weil bereits ein anderer Dialog offen ist)
        /// liefert die Methode <see cref="ContentDialogResult.None"/> zurück und wirft nicht weiter.
        /// </summary>
        /// <param name="xamlRoot">XamlRoot der aufrufenden Page – Pflicht für WinUI 3 ContentDialog.</param>
        /// <param name="title">Titelzeile des Dialogs.</param>
        /// <param name="content">Anzuzeigender Text – wird in einem ScrollViewer mit selektierbarem TextBlock dargestellt.</param>
        /// <param name="primaryButtonText">Optionaler Primärbutton (z.B. "Als TXT speichern"). Null = nur "Schließen".</param>
        /// <param name="closeButtonText">Beschriftung des "Schließen"-Buttons. Default: "Schließen".</param>
        /// <param name="maxHeight">Maximale Höhe des ScrollViewers. Default: 400.</param>
        /// <param name="useMonospace">True schaltet Consolas ein – sinnvoll für Berichte und Dateilisten.</param>
        /// <param name="monospaceFontSize">Schriftgröße bei Monospace-Modus (default 14, Berichte können kleiner laufen lassen).</param>
        /// <param name="defaultButton">Welcher Button durch Enter ausgelöst wird – wichtig bei destruktiven Aktionen.</param>
        public static async Task<ContentDialogResult> ShowAsync(
            XamlRoot xamlRoot,
            string title,
            string content,
            string? primaryButtonText = null,
            string closeButtonText = "Schließen",
            double maxHeight = 400,
            bool useMonospace = false,
            double monospaceFontSize = 14,
            ContentDialogButton defaultButton = ContentDialogButton.None)
        {
            TextBlock textBlock = new()
            {
                Text                   = content,
                TextWrapping           = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            };

            if (useMonospace)
            {
                textBlock.FontFamily   = new FontFamily("Consolas");
                textBlock.FontSize     = monospaceFontSize;
                // Berichte mit fester Spaltenbreite sollen nicht umgebrochen werden
                textBlock.TextWrapping = TextWrapping.NoWrap;
            }

            ContentDialog dialog = new()
            {
                XamlRoot        = xamlRoot,
                Title           = title,
                Content         = new ScrollViewer
                {
                    MaxHeight = maxHeight,
                    Content   = textBlock
                },
                CloseButtonText = closeButtonText,
                DefaultButton   = defaultButton
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
                // Ein anderer ContentDialog ist bereits offen – Aufrufer entscheidet,
                // wie damit umgegangen wird. None signalisiert "Dialog wurde nicht gezeigt".
                return ContentDialogResult.None;
            }
        }
    }
}
