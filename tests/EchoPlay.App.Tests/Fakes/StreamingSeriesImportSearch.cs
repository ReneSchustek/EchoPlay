using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Test-Fake für <see cref="ISeriesImportSearch"/>, der die Treffer einzeln und auf
    /// Zuruf herausgibt. Der Test bestimmt damit, wann welcher Treffer feststeht, und kann
    /// prüfen, was die Seite in der Zwischenzeit anzeigt — ohne Wartezeiten.
    /// </summary>
    internal sealed class StreamingSeriesImportSearch : ISeriesImportSearch
    {
        private readonly object _lock = new();
        private readonly List<TaskCompletionSource<ImportSeries?>> _gates = [];
        private int _published;

        /// <inheritdoc/>
        public async Task<IReadOnlyList<ImportSeries>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            List<ImportSeries> results = [];

            await foreach (ImportSeries series in SearchStreamAsync(query, cancellationToken))
            {
                results.Add(series);
            }

            return results;
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<ImportSeries> SearchStreamAsync(
            string query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            int index = 0;

            while (true)
            {
                ImportSeries? next = await Gate(index).Task.WaitAsync(cancellationToken);

                if (next is null)
                {
                    yield break;
                }

                index++;
                yield return next;
            }
        }

        /// <summary>Gibt den nächsten Treffer frei.</summary>
        /// <param name="series">Der Treffer, der jetzt feststeht.</param>
        public void Publish(ImportSeries series)
        {
            _ = Gate(_published++).TrySetResult(series);
        }

        /// <summary>Beendet den Strom — es folgen keine Treffer mehr.</summary>
        public void Complete()
        {
            _ = Gate(_published).TrySetResult(null);
        }

        private TaskCompletionSource<ImportSeries?> Gate(int index)
        {
            lock (_lock)
            {
                while (_gates.Count <= index)
                {
                    // Ohne asynchrone Fortsetzung liefe die Fortsetzung des Ansichtsmodells
                    // im Stack des Tests — der beobachtete dann seinen eigenen Fortschritt.
                    _gates.Add(new TaskCompletionSource<ImportSeries?>(TaskCreationOptions.RunContinuationsAsynchronously));
                }

                return _gates[index];
            }
        }
    }
}
