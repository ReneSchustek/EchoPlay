namespace EchoPlay.Logger.Models
{
    /// <summary>
    /// Definiert die Wichtigkeitsstufen für Log-Einträge.
    /// </summary>
    /// <remarks>
    /// Die Level sind aufsteigend nach Wichtigkeit sortiert.
    /// Beim Filtern gilt: Ein Level schließt alle höheren mit ein.
    /// </remarks>
    public enum LogLevel
    {
        /// <summary>
        /// Sehr detaillierte Informationen für tiefgehendes Debugging.
        /// </summary>
        Trace = 0,

        /// <summary>
        /// Informationen für Entwickler während der Entwicklung.
        /// </summary>
        Debug = 1,

        /// <summary>
        /// Normale Ablaufinformationen über den Anwendungszustand.
        /// </summary>
        Information = 2,

        /// <summary>
        /// Potenzielle Probleme, die Aufmerksamkeit erfordern könnten.
        /// </summary>
        Warning = 3,

        /// <summary>
        /// Fehler, die aufgetreten aber behandelt wurden.
        /// </summary>
        Error = 4,

        /// <summary>
        /// Kritische Fehler, die die Anwendung instabil machen.
        /// </summary>
        Fatal = 5
    }
}
