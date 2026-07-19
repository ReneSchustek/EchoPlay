namespace EchoPlay.Logger.Scoping
{
    /// <summary>
    /// Repräsentiert einen einzelnen Logging-Scope.
    /// Wird mit einem using-Block verwendet und räumt sich automatisch auf.
    /// </summary>
    public sealed class LogScope : IDisposable
    {
        private readonly string _name;

        /// <summary>
        /// Erstellt einen neuen Scope und aktiviert ihn sofort.
        /// </summary>
        /// <param name="name">Name des Scopes (z.B. "API:Google").</param>
        public LogScope(string name)
        {
            _name = name;
            LogScopeManager.PushScope(name);
        }

        /// <summary>
        /// Beendet den Scope und entfernt ihn vom Stack.
        /// Wird auch dann ausgeführt, wenn der using-Block durch eine Exception verlassen wird.
        /// </summary>
        public void Dispose()
        {
            try
            {
                LogScopeManager.PopScope(_name);
            }
            catch (InvalidOperationException ex)
            {
                // Eine Exception aus Dispose() würde eine bereits aktive Exception überschreiben
                // und die ursprüngliche Fehlerursache verschleiern. Deshalb wird sie hier
                // still geloggt (kein ILogger verfügbar wegen Rekursionsgefahr) und nicht weitergeworfen.
                System.Diagnostics.Trace.WriteLine($"LogScope.Dispose: Scope-Stack inkonsistent – {ex.Message}");
            }
        }
    }
}
