using EchoPlay.App.Services;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IWatchToggleService"/>. Merkt sich, für welche Serie die
    /// Überwachung auf welchen Stand gesetzt wurde.
    /// </summary>
    internal sealed class FakeWatchToggleService : IWatchToggleService
    {
        private readonly List<(Guid SeriesId, bool Watch)> _calls = [];

        /// <summary>Jede Umschaltung in Aufrufreihenfolge.</summary>
        public IReadOnlyList<(Guid SeriesId, bool Watch)> Calls => _calls;

        /// <inheritdoc/>
        public Task ToggleAsync(Guid seriesId, bool watch, CancellationToken cancellationToken = default)
        {
            _calls.Add((seriesId, watch));
            return Task.CompletedTask;
        }
    }
}
