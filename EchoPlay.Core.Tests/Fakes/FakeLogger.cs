using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Scoping;
using System;
using System.Collections.Generic;

namespace EchoPlay.Core.Tests.Fakes
{
    /// <summary>
    /// Minimaler Fake für <see cref="ILogger"/>.
    /// Verwirft alle Nachrichten – dient nur der Dependency-Erfüllung in Tests.
    /// </summary>
    internal sealed class FakeLogger : ILogger
    {
        /// <inheritdoc/>
        public bool IsDebugEnabled => false;

        /// <inheritdoc/>
        public void Trace(string message) { }

        /// <inheritdoc/>
        public void Debug(string message) { }

        /// <inheritdoc/>
        public void Info(string message) { }

        /// <inheritdoc/>
        public void Warning(string message) { }

        /// <inheritdoc/>
        public void Error(string message, Exception? exception = null) { }

        /// <inheritdoc/>
        public void Fatal(string message, Exception? exception = null) { }

        /// <inheritdoc/>
        public LogScope BeginScope(string name) => new(name);
    }
}
