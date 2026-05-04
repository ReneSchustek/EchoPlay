using EchoPlay.Core.Abstractions.Import;
using EchoPlay.Core.Models.Import;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Test-Fake für <see cref="ISeriesImportSearch"/>, der jeden Aufruf an einer
    /// <see cref="TaskCompletionSource{TResult}"/> hängen lässt. Erst wenn der Test
    /// <see cref="CompleteCall"/> aufruft, läuft die jeweilige Suche weiter. Dadurch
    /// lassen sich Reset- und Back-to-Back-Szenarien deterministisch reproduzieren,
    /// ohne <c>Task.Delay</c> oder <c>Thread.Sleep</c>.
    /// </summary>
    internal sealed class GatedSeriesImportSearch : ISeriesImportSearch
    {
        private readonly object _lock = new();
        private readonly List<TaskCompletionSource<IReadOnlyList<ImportSeries>>> _calls = [];

        /// <inheritdoc/>
        public Task<IReadOnlyList<ImportSeries>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            // RunContinuationsAsynchronously verhindert, dass die Fortsetzung der awaitenden
            // Methode synchron im SetResult-Aufruf läuft – sonst kann der Test versehentlich
            // den Fortschritt der gerade laufenden Suche im selben Stack beobachten.
            TaskCompletionSource<IReadOnlyList<ImportSeries>> tcs =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_lock)
            {
                _calls.Add(tcs);
            }

            return tcs.Task;
        }

        /// <summary>Anzahl bisher entgegengenommener Suchaufrufe.</summary>
        public int CallCount
        {
            get
            {
                lock (_lock)
                {
                    return _calls.Count;
                }
            }
        }

        /// <summary>
        /// Schließt den <paramref name="index"/>-ten Suchaufruf mit den uebergebenen
        /// Treffern ab. Erlaubt Tests, eine veraltete Suche bewusst zuerst auszuliefern,
        /// um zu prüfen, dass das ViewModel ihre Treffer verwirft.
        /// </summary>
        public void CompleteCall(int index, IReadOnlyList<ImportSeries> results)
        {
            TaskCompletionSource<IReadOnlyList<ImportSeries>> tcs;
            lock (_lock)
            {
                tcs = _calls[index];
            }
            _ = tcs.TrySetResult(results);
        }
    }
}
