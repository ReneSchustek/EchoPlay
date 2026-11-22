namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Baut die Zeilen für die Protokollanzeige aus den Einträgen des ViewModels.
    /// </summary>
    /// <remarks>
    /// Eigene Klasse, weil das Befüllen im Code-Behind der Einstellungsseite steckt und
    /// dort einen WinUI-Host bräuchte. Die Entscheidung, was angezeigt wird, ist
    /// hier testbar; im Code-Behind bleibt nur das Übertragen in den
    /// <c>RichTextBlock</c>.
    /// <para>
    /// Eine leere Anzeige darf nicht wie ein Fehler aussehen: Ohne Hinweiszeile wäre eine
    /// Seite ohne Einträge von einer kaputten nicht zu unterscheiden.
    /// </para>
    /// </remarks>
    internal static class LogViewBuilder
    {
        /// <summary>
        /// Liefert die anzuzeigenden Zeilen.
        /// </summary>
        /// <param name="entries">Die gefilterten Einträge aus dem ViewModel.</param>
        /// <returns>Mindestens eine Zeile; nie eine leere Liste.</returns>
        public static IReadOnlyList<string> BuildLines(IReadOnlyList<string>? entries)
        {
            if (entries is null || entries.Count == 0)
            {
                return new[] { SafeResourceLoader.Get("LogViewNoEntries", "Keine Protokolleinträge vorhanden.") };
            }

            return entries;
        }

        /// <summary>
        /// Die Zeile für den Fall, dass der Protokoll-Puffer beim Start nicht registriert wurde.
        /// Dann kann die Anzeige grundsätzlich nichts enthalten.
        /// </summary>
        /// <returns>Eine Zeile mit dem Hinweis auf den Neustart.</returns>
        public static IReadOnlyList<string> UnavailableLines() =>
            new[] { SafeResourceLoader.Get(
                "LogViewUnavailable",
                "Der Protokoll-Puffer ist nicht verfügbar. Starte die Anwendung neu, um die Live-Ansicht zu aktivieren.") };
    }
}
