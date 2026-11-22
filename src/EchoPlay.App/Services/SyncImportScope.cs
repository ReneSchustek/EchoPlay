namespace EchoPlay.App.Services
{
    /// <summary>
    /// Welche gefundenen Serienordner ein Einlesen als Serie anlegt.
    /// </summary>
    public enum SyncImportScope
    {
        /// <summary>
        /// Nur, wenn die Einstellung <c>AutoImportAfterScan</c> es vorsieht; sonst werden bekannte
        /// Serien abgeglichen und neue Ordner bleiben liegen.
        /// </summary>
        AsConfigured,

        /// <summary>
        /// Jeder Ordner, unabhängig von der Einstellung. Für die erste Einrichtung und die
        /// Neu-Initialisierung, nach der garantiert alle Serienordner neu angelegt werden.
        /// </summary>
        AllFolders,
    }
}
