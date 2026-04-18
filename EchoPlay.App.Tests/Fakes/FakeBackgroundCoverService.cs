using EchoPlay.App.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="BackgroundCoverService"/>, der nur die Aufrufe der
    /// Splash- bzw. Hintergrund-Phasen zählt und keinerlei DB-/Dateisystem-Arbeit ausführt.
    /// Wird in <see cref="Services.StartupValidatorTests"/> genutzt, um zu prüfen,
    /// dass der Splash-Pfad ausschliesslich <see cref="BackgroundCoverService.RunSeriesCoversOnceAsync"/>
    /// aufruft und niemals <see cref="BackgroundCoverService.RunOnceAsync"/>.
    /// </summary>
    internal sealed class FakeBackgroundCoverService : BackgroundCoverService
    {
        public FakeBackgroundCoverService(
            IServiceScopeFactory scopeFactory,
            IHttpClientFactory httpClientFactory)
            : base(
                scopeFactory,
                new CoverService(scopeFactory, new FakeLoggerFactory()),
                httpClientFactory,
                new FakeSpotifyCredentialStore(),
                new BackgroundCoverServiceOptions(),
                new FakeLoggerFactory())
        {
        }

        /// <summary>Anzahl der Aufrufe der vollen <see cref="BackgroundCoverService.RunOnceAsync"/>-Phase.</summary>
        public int RunOnceCallCount { get; private set; }

        /// <summary>Anzahl der Aufrufe der Splash-only <see cref="BackgroundCoverService.RunSeriesCoversOnceAsync"/>-Phase.</summary>
        public int RunSeriesCoversCallCount { get; private set; }

        /// <summary>Der beim letzten Aufruf übergebene <c>isOnlineAvailable</c>-Wert.</summary>
        public bool? LastIsOnlineAvailable { get; private set; }

        /// <summary>
        /// Alle beim Priority-Aufruf beobachteten Cancellation-Tokens. Der Test
        /// kann darüber prüfen, ob das VM den Abbruch sauber signalisiert hat.
        /// </summary>
        public List<CancellationToken> PriorityTokens { get; } = [];

        /// <summary>
        /// Wenn gesetzt, wartet der Priority-Aufruf auf diesen Task, bevor er zurückkehrt.
        /// Ermöglicht deterministische Tests, die den Abbruch beobachten wollen.
        /// </summary>
        public TaskCompletionSource? PriorityHold { get; set; }

        /// <inheritdoc/>
        public override Task<int> RunOnceAsync()
        {
            RunOnceCallCount++;
            return Task.FromResult(0);
        }

        /// <inheritdoc/>
        public override Task<int> RunSeriesCoversOnceAsync(bool isOnlineAvailable, CancellationToken ct = default)
        {
            RunSeriesCoversCallCount++;
            LastIsOnlineAvailable = isOnlineAvailable;
            return Task.FromResult(0);
        }

        /// <inheritdoc/>
        public override async Task RequestPriorityForSeriesAsync(System.Guid seriesId, CancellationToken ct = default)
        {
            PriorityTokens.Add(ct);

            if (PriorityHold is not null)
            {
                // Wartet bis der Test per PriorityHold.TrySetResult fortfahren lässt
                // oder das Token abgebrochen wird.
                _ = await Task.WhenAny(PriorityHold.Task, Task.Delay(Timeout.Infinite, ct)).ConfigureAwait(false);
            }
        }
    }
}
