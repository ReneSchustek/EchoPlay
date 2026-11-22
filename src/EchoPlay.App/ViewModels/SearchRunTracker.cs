namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Führt Buch über die laufenden Suchen: Jede neue Suche macht die vorige obsolet, und Tests
    /// können auf alle noch laufenden warten.
    /// </summary>
    internal sealed class SearchRunTracker : IDisposable
    {
        // Liste statt einer einzelnen Quelle: Bei Suchen unmittelbar hintereinander laufen mehrere
        // Aufrufe parallel, die alle abgewartet werden müssen. Der älteste verwirft seine
        // Ergebnisse, der neueste schreibt sie.
        private readonly object _inflightLock = new();
        private readonly List<TaskCompletionSource<bool>> _inflight = [];

        // Pro Suche neu erzeugt; abgebrochen und freigegeben am Anfang der nächsten Suche oder
        // beim Zurücksetzen, damit alte Cover-Ladevorgänge keine Anfragen für vergessene Treffer
        // mehr starten und Treffer obsolet gewordener Suchen nicht mehr in die Liste gelangen.
        private CancellationTokenSource? _currentRun;

        /// <summary>
        /// Beginnt eine Suche. Ab hier gilt jede ältere Suche als obsolet: Ihr Token meldet den Abbruch.
        /// </summary>
        /// <returns>Das Token dieser Suche und die Quelle, die <see cref="End"/> abschließt.</returns>
        public (CancellationToken Token, TaskCompletionSource<bool> Completion) Begin()
        {
            TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_inflightLock)
            {
                _inflight.Add(completion);
            }

            CancelCurrent();
            _currentRun = new CancellationTokenSource();
            return (_currentRun.Token, completion);
        }

        /// <summary>Meldet eine Suche als beendet.</summary>
        /// <param name="completion">Die Quelle aus <see cref="Begin"/>.</param>
        public void End(TaskCompletionSource<bool> completion)
        {
            ArgumentNullException.ThrowIfNull(completion);

            lock (_inflightLock)
            {
                _ = _inflight.Remove(completion);
            }
            _ = completion.TrySetResult(true);
        }

        /// <summary>
        /// Wartet auf alle gerade laufenden Suchen, auch auf eine ältere, die wegen des Abbruchs
        /// kurz vor dem Ende steht.
        /// </summary>
        /// <returns>Ein Task, der endet, wenn keine der jetzt laufenden Suchen mehr läuft.</returns>
        public Task WaitForAllAsync()
        {
            Task[] tasks;
            lock (_inflightLock)
            {
                if (_inflight.Count == 0)
                {
                    return Task.CompletedTask;
                }

                tasks = [.. _inflight.Select(tcs => tcs.Task)];
            }

            return Task.WhenAll(tasks);
        }

        /// <summary>Macht die laufende Suche obsolet und bricht ihre Cover-Ladevorgänge ab.</summary>
        public void CancelCurrent()
        {
            CancellationTokenSource? old = _currentRun;
            _currentRun = null;
            if (old is null) return;

            try { old.Cancel(); }
            catch (ObjectDisposedException)
            {
                // Bereits freigegeben; Cancel ist dann wirkungslos, das folgende Dispose
                // ist idempotent. Bewusster Schluck ohne Protokoll.
            }
            old.Dispose();
        }

        /// <inheritdoc/>
        public void Dispose() => CancelCurrent();
    }
}
