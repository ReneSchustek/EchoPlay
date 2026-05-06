using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Log-Viewer: Manuelles Refresh, Live-Timer und NumberBox-Handler für
    /// die Aufbewahrungszeit.
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        /// <summary>
        /// Liest die aktuellen Log-Einträge aus dem Puffer und scrollt ans Ende der Liste.
        /// </summary>
        private void OnRefreshLogsClick(object sender, RoutedEventArgs e)
        {
            RefreshLogView();
        }

        /// <summary>
        /// Startet den Live-Timer (2 Sekunden Intervall) für automatisches Log-Refresh.
        /// </summary>
        private void OnLiveViewChecked(object sender, RoutedEventArgs e)
        {
            if (_logLiveTimer is null)
            {
                _logLiveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _logLiveTimer.Tick += OnLogLiveTick;
            }

            _logLiveTimer.Start();
            RefreshLogView();
        }

        /// <summary>Stoppt den Live-Timer.</summary>
        private void OnLiveViewUnchecked(object sender, RoutedEventArgs e)
        {
            _logLiveTimer?.Stop();
        }

        /// <summary>
        /// Tick-Handler des Live-Timers. Benannt (statt Lambda), damit
        /// <c>OnNavigatedFrom</c> die Subscription wieder aufheben kann
        /// und die Page beim Verlassen GC-frei wird.
        /// </summary>
        private void OnLogLiveTick(object? sender, object e)
        {
            RefreshLogView();
        }

        /// <summary>
        /// Aktualisiert den RichTextBlock mit den aktuellen Log-Einträgen
        /// und scrollt ans Ende. Wird bei manuellem Refresh und Live-Updates aufgerufen.
        /// </summary>
        private void RefreshLogView()
        {
            ViewModel.RefreshLogs();

            // RichTextBlock mit den aktuellen Einträgen befüllen
            LogRichTextBlock.Blocks.Clear();

            Microsoft.UI.Xaml.Documents.Paragraph paragraph = new();

            foreach (string entry in ViewModel.LogEntries)
            {
                paragraph.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = entry });
                paragraph.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
            }

            LogRichTextBlock.Blocks.Add(paragraph);

            // Ans Ende scrollen – neueste Einträge sind unten
            LogScrollViewer.UpdateLayout();
            LogScrollViewer.ScrollToVerticalOffset(LogScrollViewer.ScrollableHeight);
        }

        /// <summary>
        /// Überträgt den neuen Zahlenwert der NumberBox in die ViewModel-Property.
        /// NumberBox.Value ist <see langword="double"/> – explizite Konvertierung in <see langword="int"/> nötig.
        /// </summary>
        private void OnLogRetentionDaysChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            // NaN tritt auf, wenn der Nutzer ein ungültiges Zeichen eingibt – ignorieren
            if (!double.IsNaN(args.NewValue))
            {
                ViewModel.LogRetentionDays = (int)args.NewValue;
            }
        }
    }
}
