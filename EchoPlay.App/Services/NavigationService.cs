using EchoPlay.App.Views;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Frame-basierte Standard-Implementierung von <see cref="INavigationService"/>.
    /// Hält eine Referenz auf den aktiven <see cref="Frame"/> der MainWindow-Shell
    /// und mappt <see cref="NavigationTarget"/>-Werte auf die konkreten Page-Typen.
    /// </summary>
    /// <remarks>
    /// Dies ist die einzige Stelle der Anwendung, die die konkreten Page-Typen kennt –
    /// ViewModels und andere Services bleiben dadurch frei von WinUI-Page-Referenzen.
    /// </remarks>
    public sealed class NavigationService : INavigationService
    {
        // Mapping logischer Ziele auf Page-Typen. Zentral an einer Stelle,
        // damit beim Hinzufügen einer neuen Seite nur hier und im Enum etwas geändert wird.
        private static readonly IReadOnlyDictionary<NavigationTarget, Type> TargetMap =
            new Dictionary<NavigationTarget, Type>
            {
                [NavigationTarget.Dashboard]       = typeof(DashboardPage),
                [NavigationTarget.MediathekOnline] = typeof(MediathekOnlinePage),
                [NavigationTarget.MediathekLokal]  = typeof(MediathekLokalPage),
                [NavigationTarget.Suche]           = typeof(SuchePage),
                [NavigationTarget.Player]          = typeof(PlayerPage),
                [NavigationTarget.Settings]        = typeof(SettingsPage),
                [NavigationTarget.TagManager]      = typeof(TagManagerPage),
                [NavigationTarget.SeriesDetail]    = typeof(SeriesDetailPage),
                [NavigationTarget.Import]          = typeof(ImportPage),
                [NavigationTarget.Statistik]       = typeof(StatistikPage),
                [NavigationTarget.Protokoll]       = typeof(ProtokollPage),
                [NavigationTarget.Ueber]           = typeof(UeberPage)
            };

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
        public void NavigateTo(NavigationTarget target, object? parameter = null)
        {
            Frame frame = _frame
                ?? throw new InvalidOperationException(
                    "NavigationService wurde vor der Initialisierung verwendet. "
                    + "MainWindow muss Initialize(Frame) vor dem ersten NavigateTo aufrufen.");

            Type pageType = TargetMap[target];

            // Identische Seite ohne Parameter: keine Doppelnavigation
            if (parameter is null && frame.Content?.GetType() == pageType)
            {
                return;
            }

            frame.Navigate(pageType, parameter);
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
