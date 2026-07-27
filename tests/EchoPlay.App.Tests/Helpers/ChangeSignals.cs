using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Helpers
{
    /// <summary>
    /// Wartehilfen für Zustände, die ein ViewModel erst nach einem „fire and forget"-Command
    /// oder einem Hintergrund-Ereignis erreicht.
    /// </summary>
    /// <remarks>
    /// Ersetzt Warteschleifen mit <c>Task.Delay</c>: Die sind unter Last erst zu kurz und dann
    /// zu langsam, und sie verstecken echte Fehler hinter einem Timeout. Hier weckt die
    /// Benachrichtigung selbst — die Frist ist nur die Reißleine, damit ein kaputter Test
    /// abbricht statt zu hängen.
    /// </remarks>
    internal static class ChangeSignals
    {
        /// <summary>Frist, nach der ein ausbleibendes Signal als Fehler gilt.</summary>
        private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Wartet, bis <paramref name="condition"/> erfüllt ist — geweckt durch
        /// <see cref="INotifyPropertyChanged.PropertyChanged"/>.
        /// </summary>
        /// <param name="source">Das beobachtete ViewModel.</param>
        /// <param name="condition">Der erwartete Zustand.</param>
        /// <param name="description">Was erwartet wurde — erscheint in der Fehlermeldung.</param>
        public static async Task WaitForAsync(
            INotifyPropertyChanged source,
            Func<bool> condition,
            string description)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(condition);

            TaskCompletionSource signal = new(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
            {
                if (condition())
                {
                    _ = signal.TrySetResult();
                }
            }

            source.PropertyChanged += OnPropertyChanged;

            try
            {
                // Zwischen Anmeldung und Prüfung kann der Zustand bereits eingetreten sein.
                if (condition())
                {
                    return;
                }

                await AwaitOrFailAsync(signal.Task, description).ConfigureAwait(false);
            }
            finally
            {
                source.PropertyChanged -= OnPropertyChanged;
            }
        }

        /// <summary>
        /// Wartet, bis <paramref name="condition"/> erfüllt ist — geweckt durch
        /// <see cref="INotifyCollectionChanged.CollectionChanged"/>.
        /// </summary>
        /// <param name="source">Die beobachtete Sammlung.</param>
        /// <param name="condition">Der erwartete Zustand.</param>
        /// <param name="description">Was erwartet wurde — erscheint in der Fehlermeldung.</param>
        /// <remarks>
        /// Eigener Name statt Überladung: <c>ObservableCollection&lt;T&gt;</c> meldet beide
        /// Schnittstellen, eine Überladung wäre für den Compiler mehrdeutig.
        /// </remarks>
        public static async Task WaitForCollectionAsync(
            INotifyCollectionChanged source,
            Func<bool> condition,
            string description)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(condition);

            TaskCompletionSource signal = new(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
            {
                if (condition())
                {
                    _ = signal.TrySetResult();
                }
            }

            source.CollectionChanged += OnCollectionChanged;

            try
            {
                if (condition())
                {
                    return;
                }

                await AwaitOrFailAsync(signal.Task, description).ConfigureAwait(false);
            }
            finally
            {
                source.CollectionChanged -= OnCollectionChanged;
            }
        }

        private static async Task AwaitOrFailAsync(Task signal, string description)
        {
            try
            {
                await signal.WaitAsync(Deadline, TestContext.Current.CancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                Assert.Fail($"Erwarteter Zustand trat innerhalb von {Deadline.TotalSeconds} s nicht ein: {description}");
            }
        }
    }

    /// <summary>
    /// <see cref="IProgress{T}"/>-Fake, der jede Meldung mitzählt und die erste signalisiert.
    /// </summary>
    /// <remarks>
    /// <see cref="Progress{T}"/> stellt seine Rückrufe über den SynchronizationContext bzw. den
    /// ThreadPool zu — wann sie eintreffen, ist nicht vorhersagbar. Dieser Fake ruft direkt auf
    /// und macht den Test damit unabhängig vom Zeitverhalten.
    /// </remarks>
    /// <typeparam name="T">Typ der Fortschrittsmeldung.</typeparam>
    internal sealed class SignalingProgress<T> : IProgress<T>
    {
        private readonly TaskCompletionSource _firstReport = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _count;

        /// <summary>Anzahl der bisher eingegangenen Meldungen.</summary>
        public int Count => Volatile.Read(ref _count);

        /// <summary>Wird abgeschlossen, sobald die erste Meldung eingeht.</summary>
        public Task FirstReport => _firstReport.Task;

        /// <inheritdoc/>
        /// <param name="value">Die gemeldete Nutzlast.</param>
        public void Report(T value)
        {
            _ = Interlocked.Increment(ref _count);
            _ = _firstReport.TrySetResult();
        }
    }
}
