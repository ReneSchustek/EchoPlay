using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace EchoPlay.App.Views
{
    /// <summary>
    /// Log-Viewer: Zeichnen des Protokollblocks und NumberBox-Handler für die
    /// Aufbewahrungszeit.
    /// </summary>
    /// <remarks>
    /// Den Takt der Live-Ansicht führt allein das
    /// <see cref="ViewModels.MaintenanceSettingsViewModel"/>. Die Seite zeichnet, sobald
    /// sich die Einträge ändern — vorher lief hier ein zweiter Zeitgeber im selben
    /// Zwei-Sekunden-Takt, der dieselben Daten noch einmal las.
    /// </remarks>
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
        /// Holt die Einträge neu aus dem Puffer und zeichnet sie. Für manuellen Refresh,
        /// Live-Timer und den ersten Aufbau der Seite.
        /// </summary>
        private void RefreshLogView()
        {
            ViewModel.MaintenanceVM.RefreshLogs();
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
                ViewModel.MaintenanceVM.LogEntries, ViewModel.MaintenanceVM.IsLogViewerAvailable))
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
        private void OnMaintenancePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ViewModel.MaintenanceVM.LogSearchText) or nameof(ViewModel.MaintenanceVM.LogMinimumLevel))
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
                ViewModel.GeneralVM.LogRetentionDays = (int)args.NewValue;
            }
        }

        /// <summary>
        /// Zeichnet den Protokollblock neu, sobald das Ansichtsmodell neue Einträge
        /// eingelesen hat — im Live-Betrieb alle zwei Sekunden, sonst auf Anforderung.
        /// </summary>
        /// <remarks>
        /// Zeichnet nur und lädt nicht: <see cref="RefreshLogView"/> würde den Puffer neu
        /// einlesen, damit erneut die Sammlung ändern und sich selbst aufrufen. Genau diese
        /// Schleife hat die Anwendung beim ersten Versuch ohne eine einzige Protokollzeile
        /// beendet.
        /// </remarks>
        private void OnLogEntriesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            RenderLogView();
        }
    }
}
