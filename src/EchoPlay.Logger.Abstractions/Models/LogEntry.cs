namespace EchoPlay.Logger.Models
{
    /// <summary>
    /// Repräsentiert einen einzelnen, unveränderlichen Log-Eintrag.
    /// </summary>
    /// <param name="Timestamp">Zeitpunkt, zu dem der Eintrag erstellt wurde — als UTC. Display-Schichten konvertieren beim Rendern auf Lokalzeit.</param>
    /// <param name="Level">Wichtigkeitsstufe des Eintrags.</param>
    /// <param name="Message">Die eigentliche Log-Nachricht.</param>
    /// <param name="Category">Quelle des Logs (z.B. "ApiService", "Database").</param>
    /// <param name="Scopes">Liste der aktiven Kontexte zum Zeitpunkt des Loggings.</param>
    /// <param name="Exception">Optionaler Fehler bei Error- oder Fatal-Einträgen.</param>
    public record LogEntry(
        DateTime Timestamp,
        LogLevel Level,
        string Message,
        string Category,
        IReadOnlyList<string> Scopes,
        Exception? Exception = null
    );
}
