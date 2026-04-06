using EchoPlay.Logger.Abstractions;

namespace EchoPlay.Core.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ILoggerFactory"/>.
    /// Gibt immer denselben <see cref="FakeLogger"/> zurück.
    /// </summary>
    internal sealed class FakeLoggerFactory : ILoggerFactory
    {
        /// <inheritdoc/>
        public ILogger CreateLogger(string category) => new FakeLogger();
    }
}
