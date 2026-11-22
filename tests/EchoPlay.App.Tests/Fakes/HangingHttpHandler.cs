using System.Diagnostics;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Gegenstelle, die nie antwortet und nur auf den Abbruch reagiert. Zählt die Versuche,
    /// damit ein Test sieht, ob die Wiederholungsregel vor ihrer Gesamtgrenze erneut anfragt.
    /// </summary>
    internal sealed class HangingHttpHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _firstAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _attempts;

        /// <summary>Erfüllt, sobald die erste Anfrage hier ankommt.</summary>
        public Task FirstAttempt => _firstAttempt.Task;

        /// <summary>Zahl der bisher angekommenen Anfragen.</summary>
        public int Attempts => Volatile.Read(ref _attempts);

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _ = Interlocked.Increment(ref _attempts);
            _ = _firstAttempt.TrySetResult();

            TaskCompletionSource neverAnswered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = cancellationToken.Register(
                () => neverAnswered.TrySetCanceled(cancellationToken));
            await neverAnswered.Task.ConfigureAwait(false);
            throw new UnreachableException();
        }
    }
}
