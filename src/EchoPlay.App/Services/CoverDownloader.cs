using EchoPlay.Logger.Abstractions;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Standard-Implementierung von <see cref="ICoverDownloader"/> über den benannten
    /// HTTP-Client <see cref="HttpClientName"/> (Resilience-Pipeline und User-Agent sind
    /// dort konfiguriert).
    /// </summary>
    public sealed class CoverDownloader : ICoverDownloader
    {
        /// <summary>
        /// Name des HTTP-Clients für Cover-Downloads. Muss zur Registrierung in
        /// <c>App.xaml.cs</c> passen; steht hier, damit der Name nur an einer Stelle lebt.
        /// </summary>
        public const string HttpClientName = "CoverDownload";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger _logger;

        /// <summary>
        /// Initialisiert den Downloader.
        /// </summary>
        /// <param name="httpClientFactory">Fabrik für benannte HTTP-Clients.</param>
        /// <param name="loggerFactory">Fabrik zur Erzeugung des Loggers.</param>
        public CoverDownloader(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(httpClientFactory);
            ArgumentNullException.ThrowIfNull(loggerFactory);

            _httpClientFactory = httpClientFactory;
            _logger = loggerFactory.CreateLogger("CoverDownloader");
        }

        /// <inheritdoc/>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "Cover-Download-Wrapper: HTTP-, Timeout-, TLS- und Redirect-Fehler einzelner URLs werden zu 'null' normalisiert, damit der Aufrufer den Eintrag überspringt und mit dem nächsten weitermacht. Echte Abbrüche sind davon ausgenommen (when-Filter).")]
        public async Task<byte[]?> DownloadAsync(string url, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return null;

            try
            {
                HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
                byte[] data = await client.GetByteArrayAsync(uri, cancellationToken).ConfigureAwait(false);

                // Leere Antwort wie einen Fehler behandeln: ein 0-Byte-Cover ist in
                // CoverImages schlimmer als kein Cover — der Platzhalter greift dann nicht
                // mehr, und die Nachlade-Läufe halten den Eintrag für erledigt.
                return data.Length > 0 ? data : null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Abbruch des Aufrufers. Ein HTTP-Timeout kommt ebenfalls als
                // TaskCanceledException, hat aber kein gesetztes Token — der fällt unten
                // in den null-Zweig.
                throw;
            }
            catch (Exception ex)
            {
                _logger.Debug(() => $"Cover-Download fehlgeschlagen: {ex.Message} URL={url}");
                return null;
            }
        }
    }
}
