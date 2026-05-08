using Microsoft.UI.Xaml.Controls;
using System;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Frame-basierte Standard-Implementierung von <see cref="INavigationService"/>.
    /// Hält eine Referenz auf den aktiven <see cref="Frame"/> der MainWindow-Shell;
    /// die Routing-Tabelle Target → Page-Typ liegt in <see cref="NavigationRouteResolver"/>.
    /// </summary>

    public sealed class NavigationService : INavigationService
    {
        private Frame? _frame;

        /// <inheritdoc/>
        public bool CanGoBack => _frame is { CanGoBack: true };

        /// <summary>
        /// Verbindet den Navigationsdienst mit dem tatsächlichen Frame der MainWindow-Shell.
        /// Wird einmalig in <see cref="MainWindow"/> nach <c>InitializeComponent()</c> aufgerufen.
        /// </summary>
        /// <param name="frame">Der ContentFrame des Hauptfensters.</param>

        public void Initialize(Frame frame)
        {
            _frame = frame;
        }

        /// <inheritdoc/>


        /// <param name="target">Parameter <c>target</c>.</param>
        /// <param name="parameter">Parameter <c>parameter</c>.</param>
        public void NavigateTo(NavigationTarget target, object? parameter = null)
        {
            Frame frame = _frame
                ?? throw new InvalidOperationException(
                    "NavigationService wurde vor der Initialisierung verwendet. "
                    + "MainWindow muss Initialize(Frame) vor dem ersten NavigateTo aufrufen.");

            Type pageType = NavigationRouteResolver.Resolve(target);

            // Identische Seite ohne Parameter: keine Doppelnavigation
            if (parameter is null && frame.Content?.GetType() == pageType)
            {
                return;
            }

            _ = frame.Navigate(pageType, parameter);
        }

        /// <inheritdoc/>
        public bool GoBack()
        {
            if (_frame is not { CanGoBack: true })
            {
                return false;
            }

            _frame.GoBack();
            return true;
        }
    }
}
