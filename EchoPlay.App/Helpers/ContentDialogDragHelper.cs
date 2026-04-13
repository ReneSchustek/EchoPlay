using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using System;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Macht einen <see cref="ContentDialog"/> per Drag verschiebbar.
    /// Der Nutzer kann den Dialog am Titelbereich greifen und frei positionieren,
    /// um verdeckte Inhalte im Hintergrund einzusehen (z.B. Cover-Kacheln bei der Cover-Auswahl).
    /// </summary>
    /// <remarks>
    /// WinUI 3 ContentDialog unterstützt kein natives Dragging. Dieser Helper nutzt
    /// <see cref="TranslateTransform"/> auf dem Dialog-Root, um die Position per
    /// Pointer-Events zu verschieben. Der Dialog kehrt bei erneutem Öffnen automatisch
    /// zur Standardposition zurück, weil die Transform bei jedem Aufruf frisch erzeugt wird.
    /// </remarks>
    public static class ContentDialogDragHelper
    {
        /// <summary>Höhe des Titelbereichs in Pixeln – nur dort soll Drag starten.</summary>
        private const double DialogTitleBarHeight = 60.0;
        /// <summary>
        /// Aktiviert Drag-Funktionalität auf dem angegebenen ContentDialog.
        /// Muss vor <c>ShowAsync()</c> aufgerufen werden.
        /// </summary>
        /// <param name="dialog">Der verschiebbar zu machende Dialog.</param>
        public static void MakeDraggable(ContentDialog dialog)
        {
            ArgumentNullException.ThrowIfNull(dialog);
            TranslateTransform transform = new();
            Point dragStart = default;
            bool isDragging = false;

            // Das Loaded-Event feuert nachdem der Dialog gerendert ist –
            // erst dann ist der visuelle Baum verfügbar.
            dialog.Loaded += (sender, args) =>
            {
                dialog.RenderTransform = transform;

                // Pointer-Events auf dem gesamten Dialog registrieren.
                // PointerPressed im Titelbereich startet den Drag.
                dialog.PointerPressed += (s, e) =>
                {
                    // Nur im oberen Bereich (Titel) den Drag starten – nicht im Content
                    Point position = e.GetCurrentPoint((UIElement)s).Position;
                    if (position.Y > DialogTitleBarHeight)
                    {
                        return;
                    }

                    isDragging = true;
                    dragStart = position;
                    _ = ((UIElement)s).CapturePointer(e.Pointer);
                };

                dialog.PointerMoved += (s, e) =>
                {
                    if (!isDragging)
                    {
                        return;
                    }

                    Point current = e.GetCurrentPoint((UIElement)s).Position;
                    transform.X += current.X - dragStart.X;
                    transform.Y += current.Y - dragStart.Y;
                };

                dialog.PointerReleased += (s, e) =>
                {
                    if (isDragging)
                    {
                        isDragging = false;
                        ((UIElement)s).ReleasePointerCapture(e.Pointer);
                    }
                };
            };
        }
    }
}
