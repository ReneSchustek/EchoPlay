using EchoPlay.Logger.Abstractions;

namespace EchoPlay.LocalLibrary.Tests.Fakes
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
