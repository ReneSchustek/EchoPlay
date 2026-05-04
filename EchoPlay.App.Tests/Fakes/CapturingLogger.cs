using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Scoping;
using System;
using System.Collections.Generic;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Test-Fake für <see cref="ILogger"/>, der jeden Aufruf mit Level, Nachricht
    /// und Exception erfasst. Für Tests, die den Log-Pfad prüfen müssen.
    /// </summary>
    internal sealed class CapturingLogger : ILogger
    {
        public List<(string Level, string Message, Exception? Exception)> Entries { get; } = [];

        public bool IsDebugEnabled => true;

        public void Trace(string message) => Entries.Add(("Trace", message, null));
        public void Debug(string message) => Entries.Add(("Debug", message, null));
        public void Info(string message) => Entries.Add(("Info", message, null));
        public void Warning(string message) => Entries.Add(("Warning", message, null));
        public void Error(string message, Exception? exception = null) => Entries.Add(("Error", message, exception));
        public void Fatal(string message, Exception? exception = null) => Entries.Add(("Fatal", message, exception));
        public LogScope BeginScope(string name) => new(name);
    }
}
