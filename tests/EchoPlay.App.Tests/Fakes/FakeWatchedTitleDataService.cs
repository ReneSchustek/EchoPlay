using EchoPlay.Data.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IWatchedTitleDataService"/> mit einer Menge im Speicher.
    /// Normalisiert wie der echte Service, damit Tests dieselben Vergleichsformen sehen.
    /// </summary>
    internal sealed class FakeWatchedTitleDataService : IWatchedTitleDataService
    {
        private readonly HashSet<string> _titles = new(StringComparer.Ordinal);

        /// <summary>Setzt einen Ausgangsbestand, als hätte der Nutzer die Titel früher überwacht.</summary>
        public void Seed(params string[] titles)
        {
            foreach (string title in titles)
            {
                _ = _titles.Add(EchoPlay.Core.Scoring.HoerspielTextNormalizer.Normalize(title));
            }
        }

        /// <inheritdoc/>
        public Task<IReadOnlySet<string>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(_titles);

        /// <inheritdoc/>
        public Task RememberAsync(string title, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(title))
            {
                _ = _titles.Add(EchoPlay.Core.Scoring.HoerspielTextNormalizer.Normalize(title));
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task ForgetAsync(string title, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(title))
            {
                _ = _titles.Remove(EchoPlay.Core.Scoring.HoerspielTextNormalizer.Normalize(title));
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Der Fake kennt keine Serien-Tabelle; der Abgleich hat hier nichts nachzutragen.
        /// Tests, die den Abgleich prüfen, laufen gegen den echten Service in EchoPlay.Data.Tests.
        /// </remarks>
        public Task<int> SyncFromWatchedSeriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}
