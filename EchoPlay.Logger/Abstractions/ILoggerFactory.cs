namespace EchoPlay.Logger.Abstractions
{
    /// <summary>
    /// Erstellt Logger-Instanzen für verschiedene Kategorien.
    /// </summary>
    public interface ILoggerFactory
    {
        /// <summary>
        /// Erstellt einen Logger für die angegebene Kategorie.
        /// </summary>
        /// <param name="category">Die Kategorie/Quelle der Logs.</param>
        /// <returns>Eine neue Logger-Instanz.</returns>
        ILogger CreateLogger(string category);
    }
}
