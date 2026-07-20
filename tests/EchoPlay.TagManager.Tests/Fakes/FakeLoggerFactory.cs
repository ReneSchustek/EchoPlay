using EchoPlay.Logger.Abstractions;

namespace EchoPlay.TagManager.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ILoggerFactory"/> – gibt immer einen <see cref="FakeLogger"/> zurück.
    /// </summary>
    internal sealed class FakeLoggerFactory : ILoggerFactory
    {
        /// <inheritdoc/>
        public ILogger CreateLogger(string category) => new FakeLogger();
    }
}
