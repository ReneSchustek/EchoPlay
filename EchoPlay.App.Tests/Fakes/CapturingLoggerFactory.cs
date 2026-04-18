using EchoPlay.Logger.Abstractions;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Gibt für jede Kategorie denselben <see cref="CapturingLogger"/> zurück,
    /// damit Tests alle Log-Einträge zentral prüfen können.
    /// </summary>
    internal sealed class CapturingLoggerFactory(CapturingLogger logger) : ILoggerFactory
    {
        private readonly CapturingLogger _logger = logger;

        public CapturingLogger Logger => _logger;

        /// <inheritdoc/>
        public ILogger CreateLogger(string category) => _logger;
    }
}
