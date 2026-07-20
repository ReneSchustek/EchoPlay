using System;
using System.Threading;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Verhindert, dass mehrere ContentDialoge gleichzeitig geöffnet werden.
    /// WinUI 3 erlaubt nur einen aktiven Dialog pro XamlRoot — doppeltes Öffnen wirft
    /// eine COMException. Dieser Guard wird von allen Pages geteilt.
    /// </summary>
    internal sealed class DialogReentrancyGuard : IDisposable
    {
        private static int _openCount;

        /// <summary>
        /// Versucht den Guard zu erwerben. Gibt <see langword="null"/> zurück wenn bereits
        /// ein Dialog offen ist — der Aufrufer sollte in diesem Fall sofort abbrechen.
        /// </summary>
        public static DialogReentrancyGuard? TryAcquire()
        {
            if (Interlocked.Increment(ref _openCount) > 1)
            {
                _ = Interlocked.Decrement(ref _openCount);
                return null;
            }

            return new DialogReentrancyGuard();
        }

        /// <inheritdoc/>
        public void Dispose() => Interlocked.Decrement(ref _openCount);
    }
}
