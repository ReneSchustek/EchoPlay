namespace EchoPlay.Logger.Core
{
    /// <summary>
    /// Notfall-Logging-Kanal für Code-Pfade, in denen die regulären
    /// <see cref="Abstractions.ILogger"/>-Sinks noch nicht oder nicht mehr
    /// verfügbar sind (z. B. App-Startfehler vor DI-Init, App-Shutdown
    /// nach Sink-Dispose).
    /// </summary>
    public static class EmergencyTrace
    {
        /// <summary>
        /// Schreibt eine Notfall-Nachricht. Bewusst Trace statt Debug, damit die Ausgabe
        /// auch im Release-Build erhalten bleibt.
        /// </summary>
        /// <param name="message">Die Nachricht, typischerweise mit Exception-Details.</param>
        public static void Log(string message) => System.Diagnostics.Trace.WriteLine(message);
    }
}
