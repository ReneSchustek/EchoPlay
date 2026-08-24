using EchoPlay.App.Services;
using EchoPlay.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fehlerdialog, der selbst scheitert. Bildet den Fall ab, dass die Meldung nicht mehr
    /// angezeigt werden kann — etwa weil das Fenster gerade schließt.
    /// </summary>
    internal sealed class ThrowingErrorDialogService : IErrorDialogService
    {
        /// <summary>Wie oft eine Anzeige versucht wurde.</summary>
        public int AttemptCount { get; private set; }

        /// <inheritdoc/>
        public Task ShowAsync(string title, string message, DialogKey key, CancellationToken cancellationToken = default)
        {
            AttemptCount++;
            throw new InvalidOperationException("Der Dialog konnte nicht angezeigt werden.");
        }

        /// <inheritdoc/>
        public Task ShowAlwaysAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            AttemptCount++;
            throw new InvalidOperationException("Der Dialog konnte nicht angezeigt werden.");
        }
    }
}
