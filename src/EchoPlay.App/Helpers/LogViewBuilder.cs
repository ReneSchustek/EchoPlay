using System.Collections.Generic;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Baut die Zeilen für die Protokollanzeige aus den Einträgen des ViewModels.
    /// </summary>
    /// <remarks>
    /// Eigene Klasse, weil das Befüllen im Code-Behind der Einstellungsseite steckt und
    /// dort einen WinUI-Host bräuchte. Die Entscheidung, <em>was</em> angezeigt wird, ist
    /// hier testbar; im Code-Behind bleibt nur das Übertragen in den
    /// <c>RichTextBlock</c>.
    /// <para>
    /// Eine leere Anzeige darf nicht wie ein Fehler aussehen: Genau das war die Meldung
    /// „Protokollseite bleibt beim Öffnen leer" — dort war es tatsächlich ein Fehler, aber
    /// ohne Hinweiszeile ist beides von außen nicht zu unterscheiden.
    /// </para>
    /// </remarks>
    internal static class LogViewBuilder
    {
        /// <summary>
        /// Liefert die anzuzeigenden Zeilen.
        /// </summary>
        /// <param name="entries">Die gefilterten Einträge aus dem ViewModel.</param>
        /// <param name="isViewerAvailable">
        /// <see langword="false"/>, wenn der Protokoll-Puffer beim Start nicht registriert
        /// wurde — dann kann die Anzeige grundsätzlich nichts enthalten.
        /// </param>
        /// <returns>Mindestens eine Zeile; nie eine leere Liste.</returns>
        public static IReadOnlyList<string> BuildLines(IReadOnlyList<string>? entries, bool isViewerAvailable)
        {
            if (!isViewerAvailable)
            {
                return [SafeResourceLoader.Get(
                    "LogViewUnavailable",
                    "Der Protokoll-Puffer ist nicht verfügbar. Starte die Anwendung neu, um die Live-Ansicht zu aktivieren.")];
            }

            if (entries is null || entries.Count == 0)
            {
                return [SafeResourceLoader.Get("LogViewNoEntries", "Keine Protokolleinträge vorhanden.")];
            }

            return entries;
        }
    }
}
