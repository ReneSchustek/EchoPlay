using EchoPlay.App.Services;
using EchoPlay.Core.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IErrorDialogService"/>.
    /// Zeichnet gezeigte Dialoge auf, ohne WinUI-3-ContentDialogs zu erzeugen.
    /// </summary>
    internal sealed class FakeErrorDialogService : IErrorDialogService
    {
        private readonly TaskCompletionSource<(string Title, string Message)> _firstDialog =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Alle gezeigten Dialoge als (Title, Message)-Tupel.</summary>
        public List<(string Title, string Message)> ShownDialogs { get; } = [];

        /// <summary>Kennungen der gezeigten Dialoge, in derselben Reihenfolge.</summary>
        public List<DialogKey> ShownKeys { get; } = [];

        /// <summary>
        /// Wird abgeschlossen, sobald der erste Dialog erscheint.
        /// </summary>
        /// <remarks>
        /// Wartepunkt für Abläufe, die aus einem Befehl heraus starten und deshalb keinen
        /// Rückgabewert haben, auf den ein Test warten könnte. Ohne ihn bliebe nur eine
        /// Wartezeit — und die macht den Test von der Laufgeschwindigkeit abhängig.
        /// </remarks>
        public Task<(string Title, string Message)> FirstDialogShown => _firstDialog.Task;

        /// <inheritdoc/>
        public Task ShowAsync(string title, string message, DialogKey key, CancellationToken cancellationToken = default)
        {
            ShownKeys.Add(key);
            return Record(title, message);
        }

        /// <inheritdoc/>
        public Task ShowAlwaysAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            ShownKeys.Add(DialogKey.None);
            return Record(title, message);
        }

        private Task Record(string title, string message)
        {
            ShownDialogs.Add((title, message));
            _ = _firstDialog.TrySetResult((title, message));
            return Task.CompletedTask;
        }
    }
}
