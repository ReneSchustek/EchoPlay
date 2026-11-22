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
    /// um verdeckte Inhalte im Hintergrund einzusehen, etwa die Cover-Kacheln bei der Cover-Auswahl.
    /// </summary>
    /// <remarks>
    /// WinUI 3 ContentDialog unterstützt kein natives Dragging. Dieser Helper nutzt
    /// <see cref="TranslateTransform"/> auf dem Dialog-Root, um die Position per
    /// Pointer-Events zu verschieben. Der Dialog kehrt bei erneutem Öffnen automatisch
    /// zur Standardposition zurück, weil die Transform bei jedem Aufruf frisch erzeugt wird.
    /// </remarks>
    internal static class ContentDialogDragHelper
    {
        /// <summary>Höhe des Titelbereichs in Pixeln; nur dort soll Drag starten.</summary>
        private const double DialogTitleBarHeight = 60.0;

        /// <summary>
        /// Aktiviert Drag-Funktionalität auf dem angegebenen ContentDialog.
        /// Muss vor <c>ShowAsync()</c> aufgerufen werden.
        /// </summary>
        /// <param name="dialog">Der verschiebbar zu machende Dialog.</param>
        public static void MakeDraggable(ContentDialog dialog)
        {
            ArgumentNullException.ThrowIfNull(dialog);
            DialogDrag drag = new();

            // Das Loaded-Event feuert, nachdem der Dialog gerendert ist,
            // erst dann ist der visuelle Baum verfügbar.
            dialog.Loaded += (sender, args) =>
            {
                dialog.RenderTransform = drag.Transform;
                dialog.PointerPressed += drag.OnPointerPressed;
                dialog.PointerMoved += drag.OnPointerMoved;
                dialog.PointerReleased += drag.OnPointerReleased;
            };
        }

        /// <summary>
        /// Zustand eines Ziehvorgangs: Verschiebung, Startpunkt und ob gerade gezogen wird.
        /// </summary>
        private sealed class DialogDrag
        {
            private Point _dragStart;
            private bool _isDragging;

            public TranslateTransform Transform { get; } = new();

            public void OnPointerPressed(object sender, PointerRoutedEventArgs e)
            {
                // Nur der Titelbereich zieht; im Inhalt soll ein Klick die Kacheln treffen.
                UIElement element = (UIElement)sender;
                Point position = e.GetCurrentPoint(element).Position;
                if (position.Y > DialogTitleBarHeight)
                {
                    return;
                }

                _isDragging = true;
                _dragStart = position;
                _ = element.CapturePointer(e.Pointer);
            }

            public void OnPointerMoved(object sender, PointerRoutedEventArgs e)
            {
                if (!_isDragging)
                {
                    return;
                }

                Point current = e.GetCurrentPoint((UIElement)sender).Position;
                Transform.X += current.X - _dragStart.X;
                Transform.Y += current.Y - _dragStart.Y;
            }

            public void OnPointerReleased(object sender, PointerRoutedEventArgs e)
            {
                if (!_isDragging)
                {
                    return;
                }

                _isDragging = false;
                ((UIElement)sender).ReleasePointerCapture(e.Pointer);
            }
        }
    }
}
