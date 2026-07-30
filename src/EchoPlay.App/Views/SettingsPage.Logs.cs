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
        /// Holt die Einträge neu aus dem Puffer und zeichnet sie. Für manuellen Refresh,
        /// Live-Timer und den ersten Aufbau der Seite.
        /// </summary>
        private void RefreshLogView()
        {
            ViewModel.RefreshLogs();
            RenderLogView();
        }

        /// <summary>
        /// Zeichnet <see cref="ViewModel"/>.<c>LogEntries</c> in den RichTextBlock und scrollt
        /// ans Ende.
        /// </summary>
        /// <remarks>
        /// Getrennt von <see cref="RefreshLogView"/>, weil es zwei verschiedene Anlässe gibt:
        /// Der Filter lässt das ViewModel schon selbst neu filtern (Setter von
        /// <c>LogSearchText</c>) und braucht danach nur noch das Zeichnen — sonst würde jeder
        /// Tastendruck den Puffer zweimal durchlaufen. Ohne diese Trennung blieb der Filter
        /// wirkungslos, bis jemand „Aktualisieren" drückte: Die Anzeige hängt nicht am
        /// ViewModel, sie wird hier von Hand gefüllt.
        /// </remarks>
        private void RenderLogView()
        {
            // RichTextBlock mit den aktuellen Einträgen befüllen
            LogRichTextBlock.Blocks.Clear();

            Microsoft.UI.Xaml.Documents.Paragraph paragraph = new();

            foreach (string entry in EchoPlay.App.Helpers.LogViewBuilder.BuildLines(
                ViewModel.LogEntries, ViewModel.IsLogViewerAvailable))
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
        /// Zeichnet die Protokollanzeige neu, wenn sich Suchtext oder Mindest-Level geändert
        /// haben. Ohne das wirkte der Filter erst nach einem Klick auf „Aktualisieren", weil die
        /// Anzeige nicht am ViewModel hängt, sondern von Hand gefüllt wird.
        /// </summary>
        /// <remarks>
        /// Hier steht bewusst <see cref="RefreshLogView"/> und nicht das reine
        /// <see cref="RenderLogView"/>, obwohl der Setter im ViewModel selbst schon neu filtert:
        /// <c>SetProperty</c> meldet die Änderung, <b>bevor</b> es <c>RefreshLogs()</c> aufruft.
        /// Wer hier nur zeichnet, malt den vorherigen Filterstand — die Anzeige hängt dann
        /// dauerhaft eine Eingabe zurück. Der zweite Filterlauf über maximal 100 gepufferte
        /// Zeilen ist der Preis dafür, nicht von dieser Reihenfolge abzuhängen.
        /// </remarks>
        /// <param name="sender">Das ViewModel.</param>
        /// <param name="e">Enthält den Namen der geänderten Eigenschaft.</param>
        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ViewModel.LogSearchText) or nameof(ViewModel.LogMinimumLevel))
            {
                RefreshLogView();
            }
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
