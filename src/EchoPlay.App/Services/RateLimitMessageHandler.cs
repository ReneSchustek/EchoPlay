using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// HTTP-Message-Handler, der vor jeder ausgehenden Anfrage den
    /// <see cref="IHostRateLimiter"/> konsultiert. Die Host-basierte Drosselung
    /// greift so einheitlich für alle HttpClient-Konsumenten, ohne dass der
    /// Aufrufer den Rate-Limiter selbst kennen muss.
    /// </summary>
    /// <remarks>
    /// In der Resilience-Pipeline innerhalb des <c>StandardResilienceHandler</c>
    /// registrieren — so wird jeder Retry-Versuch erneut gedrosselt.
    /// </remarks>
    public sealed class RateLimitMessageHandler : DelegatingHandler
    {
        private readonly IHostRateLimiter _rateLimiter;

        /// <summary>
        /// Erstellt den Handler mit dem zentralen <paramref name="rateLimiter"/>.
        /// </summary>
        /// <param name="rateLimiter">Host-basierter Rate-Limiter, typischerweise als Singleton registriert.</param>
        public RateLimitMessageHandler(IHostRateLimiter rateLimiter)
        {
            _rateLimiter = rateLimiter;
        }

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            string? host = request.RequestUri?.Host;
            if (!string.IsNullOrEmpty(host))
            {
                await _rateLimiter.WaitAsync(host, cancellationToken).ConfigureAwait(false);
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
