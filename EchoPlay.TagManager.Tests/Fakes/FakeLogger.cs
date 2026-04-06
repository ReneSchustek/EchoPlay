using EchoPlay.Logger.Abstractions;
using EchoPlay.Logger.Scoping;

namespace EchoPlay.TagManager.Tests.Fakes
{
    /// <summary>
    /// Minimaler Fake für <see cref="ILogger"/> – verwirft alle Nachrichten.
    /// </summary>
    internal sealed class FakeLogger : ILogger
    {
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
