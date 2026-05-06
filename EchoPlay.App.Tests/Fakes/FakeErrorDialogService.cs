using EchoPlay.App.Services;
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
        /// <summary>Alle gezeigten Dialoge als (Title, Message)-Tupel.</summary>
        public List<(string Title, string Message)> ShownDialogs { get; } = [];

        /// <inheritdoc/>
        public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            ShownDialogs.Add((title, message));
            return Task.CompletedTask;
        }
    }
}
