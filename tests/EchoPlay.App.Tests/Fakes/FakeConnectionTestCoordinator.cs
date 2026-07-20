using EchoPlay.App.Services;
using EchoPlay.Data.Entities.Settings;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IConnectionTestCoordinator"/>. Gibt ein vorkonfiguriertes Ergebnis
    /// zurück und zeichnet die abgefragten Provider auf, damit Tests das Verhalten des
    /// Aufrufers prüfen können, ohne echte API-Clients zu benötigen.
    /// </summary>
    internal sealed class FakeConnectionTestCoordinator : IConnectionTestCoordinator
    {
        private readonly ConnectionTestResult _result;

        /// <summary>Erzeugt einen Fake mit Standard-Erfolgsergebnis.</summary>
        public FakeConnectionTestCoordinator()
            : this(new ConnectionTestResult(true, null))
        {
        }

        /// <summary>Erzeugt einen Fake mit dem angegebenen Ergebnis.</summary>
        /// <param name="result">Das bei jedem Aufruf zurückzugebende Ergebnis.</param>
        public FakeConnectionTestCoordinator(ConnectionTestResult result)
        {
            _result = result;
        }

        /// <summary>Aufgezeichnete Aufrufe.</summary>
        public List<ProviderType> Calls { get; } = [];

        /// <inheritdoc/>
        public Task<ConnectionTestResult> TestAsync(ProviderType provider, CancellationToken cancellationToken = default)
        {
            Calls.Add(provider);
            return Task.FromResult(_result);
        }
    }
}
