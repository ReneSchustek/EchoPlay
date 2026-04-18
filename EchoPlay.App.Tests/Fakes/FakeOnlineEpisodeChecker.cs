using EchoPlay.Core.Abstractions;
using EchoPlay.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IOnlineEpisodeChecker"/>.
    /// Gibt konfigurierbare Ergebnisse zurück, ohne die iTunes API aufzurufen.
    /// </summary>
    internal sealed class FakeOnlineEpisodeChecker : IOnlineEpisodeChecker
    {
        private readonly IReadOnlyList<OnlineEpisodeCheckResult> _results;

        /// <summary>
        /// Erstellt den Fake mit optionalen festen Ergebnissen.
        /// </summary>
        /// <param name="results">Die zurückzugebenden Prüfergebnisse. Standard: leere Liste.</param>
        public FakeOnlineEpisodeChecker(IReadOnlyList<OnlineEpisodeCheckResult>? results = null)
        {
            _results = results ?? [];
        }

        /// <summary>Summe der Aufrufe aller Check-Methoden – für Assertions in Tests.</summary>
        public int CheckCallCount { get; private set; }

        /// <inheritdoc/>
        public Task<IReadOnlyList<OnlineEpisodeCheckResult>> CheckAllAsync(
            IReadOnlyList<CheckableSeriesInfo> subscribedSeries,
            CancellationToken cancellationToken = default)
        {
            CheckCallCount++;
            return Task.FromResult(_results);
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<OnlineEpisodeCheckResult>> CheckNewReleasesAsync(
            IReadOnlyList<CheckableSeriesInfo> subscribedSeries,
            DateTime cutoffDate,
            CancellationToken cancellationToken = default)
        {
            CheckCallCount++;
            return Task.FromResult(_results);
        }
    }
}
