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
    }
}
