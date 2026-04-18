using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EchoPlay.Logger.Abstractions;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Schreibt pro HTTP-Aufruf zwei zusammenfassende Log-Zeilen: eine Request-
    /// und eine Response-Zeile mit voller URL (ohne Secrets), Status-Code, Dauer,
    /// Client-Name und Versuchszähler. Der Handler ist als innerster
    /// <see cref="DelegatingHandler"/> in der Resilience-Pipeline vorgesehen,
    /// damit Retry-Versuche einzeln sichtbar werden.
    /// </summary>
    /// <remarks>
    /// Redaktion: Authorization-Header werden nie geloggt (nur Request-Zeile mit
    /// Methode + URL); bekannte sensible Query-Parameter (Token, Keys, Secrets)
    /// werden in der geloggten URL durch <c>***</c> ersetzt.
    /// </remarks>
    public sealed class HttpRequestLoggingHandler : DelegatingHandler
    {
        private static readonly HttpRequestOptionsKey<int> AttemptCountKey =
            new("EchoPlay.Http.AttemptCount");

        // Query-Parameter-Namen, die Tokens, Keys oder Kennwörter transportieren.
        // Werden in Info-Logs durch *** ersetzt, damit Support-Logs weitergegeben werden können.
        private static readonly HashSet<string> SensitiveQueryKeys =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "access_token",
                "apikey",
                "api_key",
                "auth",
                "authorization",
                "client_secret",
                "id_token",
                "key",
                "password",
                "refresh_token",
                "secret",
                "token",
            };

        private readonly ILogger _logger;
        private readonly string _clientName;

        /// <summary>
        /// Erstellt den Handler mit einem kategorisierten Logger pro HttpClient-Name.
        /// </summary>
        /// <param name="loggerFactory">Zentraler Logger-Factory aus dem DI-Container.</param>
        /// <param name="clientName">Logischer Name des HttpClients (Named- oder Typed-Client).</param>
        public HttpRequestLoggingHandler(ILoggerFactory loggerFactory, string clientName)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _clientName = string.IsNullOrEmpty(clientName) ? "HttpClient" : clientName;
            _logger = loggerFactory.CreateLogger($"Http.{_clientName}");
        }

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            int attempt = IncrementAttempt(request);
            string method = request.Method.Method;
            string redactedUrl = RedactUrl(request.RequestUri);

            _logger.Info(FormatRequestLine(method, redactedUrl, attempt));

            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                sw.Stop();

                string line = FormatResponseLine(
                    (int)response.StatusCode,
                    response.ReasonPhrase,
                    sw.ElapsedMilliseconds,
                    attempt);

                if ((int)response.StatusCode >= 400)
                {
                    _logger.Warning(line);
                }
                else
                {
                    _logger.Info(line);
                }

                return response;
            }
            catch (OperationCanceledException)
            {
                // Abbruch durch CancellationToken ist kein Fehler – nur Info.
                sw.Stop();
                _logger.Info(FormatCancelLine(sw.ElapsedMilliseconds, attempt));
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.Warning(FormatFailureLine(ex, sw.ElapsedMilliseconds, attempt));
                throw;
            }
        }

        private string FormatRequestLine(string method, string redactedUrl, int attempt)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"HTTP {method} {redactedUrl}  (Client: {_clientName}, Attempt: {attempt})");
        }

        private string FormatResponseLine(int statusCode, string? reasonPhrase, long elapsedMs, int attempt)
        {
            string reason = string.IsNullOrWhiteSpace(reasonPhrase) ? string.Empty : $" {reasonPhrase}";
            return string.Create(
                CultureInfo.InvariantCulture,
                $"   -> {statusCode}{reason} in {elapsedMs} ms  (Client: {_clientName}, Attempts: {attempt})");
        }

        private string FormatFailureLine(Exception ex, long elapsedMs, int attempt)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"   -> Fehler nach {elapsedMs} ms: {ex.GetType().Name}: {ex.Message}  (Client: {_clientName}, Attempts: {attempt})");
        }

        private string FormatCancelLine(long elapsedMs, int attempt)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"   -> Abgebrochen nach {elapsedMs} ms  (Client: {_clientName}, Attempts: {attempt})");
        }

        private static int IncrementAttempt(HttpRequestMessage request)
        {
            // HttpRequestMessage.Options bleibt bei Polly-Retries auf derselben Instanz erhalten.
            // Sollte ein Retry-Handler die Nachricht klonen, startet der Zähler wieder bei 1 –
            // das entspricht dann der Sichtweise „neue Message, neuer Versuch".
            int next = 1;
            if (request.Options.TryGetValue(AttemptCountKey, out int previous))
            {
                next = previous + 1;
            }
            request.Options.Set(AttemptCountKey, next);
            return next;
        }

        /// <summary>
        /// Erzeugt die geloggte URL-Darstellung: Schema, Host, Pfad und Query bleiben
        /// erhalten, sensible Query-Parameter werden durch <c>***</c> ersetzt.
        /// </summary>
        /// <param name="uri">Ziel-URI der Anfrage; <see langword="null"/> bei Handler-Tests.</param>
        /// <returns>Für Logs geeignete URL-Repräsentation.</returns>
        internal static string RedactUrl(Uri? uri)
        {
            if (uri is null)
            {
                return "(unbekannte URL)";
            }

            string baseUrl = uri.IsAbsoluteUri
                ? uri.GetLeftPart(UriPartial.Path)
                : uri.ToString();

            string query = uri.IsAbsoluteUri ? uri.Query : string.Empty;
            if (string.IsNullOrEmpty(query))
            {
                return baseUrl;
            }

            StringBuilder sb = new(baseUrl.Length + query.Length);
            _ = sb.Append(baseUrl);
            _ = sb.Append('?');

            string raw = query.TrimStart('?');
            string[] pairs = raw.Split('&', StringSplitOptions.None);
            bool first = true;
            foreach (string pair in pairs)
            {
                if (pair.Length == 0)
                {
                    continue;
                }

                if (!first)
                {
                    _ = sb.Append('&');
                }
                first = false;

                int eq = pair.IndexOf('=', StringComparison.Ordinal);
                string key = eq >= 0 ? pair[..eq] : pair;
                string value = eq >= 0 ? pair[(eq + 1)..] : string.Empty;

                _ = sb.Append(key);
                if (eq < 0)
                {
                    continue;
                }

                _ = sb.Append('=');
                if (SensitiveQueryKeys.Contains(key))
                {
                    _ = sb.Append("***");
                }
                else
                {
                    _ = sb.Append(value);
                }
            }

            return sb.ToString();
        }
    }
}
