using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Scoping;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Test-Fake für <see cref="ILogger"/>, der jeden Aufruf mit Level, Nachricht
    /// und Exception erfasst. Für Tests, die den Log-Pfad prüfen müssen.
    /// </summary>
    /// <remarks>
    /// Hintergrunddienste schreiben weiter, während der Test schon prüft. Deshalb wird unter einer
    /// Sperre geschrieben, und die Eigenschaften liefern eine Momentaufnahme. Eine offene Liste
    /// führte beim Aufzählen sporadisch zu „Collection was modified“.
    /// </remarks>
    internal sealed class CapturingLogger : ILogger
    {
        private readonly Lock _gate = new();
        private readonly List<(string Level, string Message, Exception? Exception)> _entries = [];
        private readonly List<string> _scopeNames = [];

        public IReadOnlyList<(string Level, string Message, Exception? Exception)> Entries
        {
            get
            {
                lock (_gate)
                {
                    return [.. _entries];
                }
            }
        }

        /// <summary>
        /// Erfasst alle BeginScope-Aufrufe in der Reihenfolge ihrer Eröffnung.
        /// Damit prüfen Tests gegen die JobScopes-Konstanten, ohne die Scope-Mechanik selbst zu testen.
        /// </summary>
        public IReadOnlyList<string> ScopeNames
        {
            get
            {
                lock (_gate)
                {
                    return [.. _scopeNames];
                }
            }
        }

        public bool IsDebugEnabled => true;

        public void Trace(string message) => Add("Trace", message, null);
        public void Debug(string message) => Add("Debug", message, null);
        public void Info(string message) => Add("Info", message, null);
        public void Warning(string message) => Add("Warning", message, null);
        public void Error(string message, Exception? exception = null) => Add("Error", message, exception);
        public void Fatal(string message, Exception? exception = null) => Add("Fatal", message, exception);

        public LogScope BeginScope(string name)
        {
            lock (_gate)
            {
                _scopeNames.Add(name);
            }

            return new(name);
        }

        private void Add(string level, string message, Exception? exception)
        {
            lock (_gate)
            {
                _entries.Add((level, message, exception));
            }
        }
    }
}
