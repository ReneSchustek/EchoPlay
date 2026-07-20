using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Gemeinsame Basis für Cover-Such-Services, die eine JSON-API abfragen und die Treffer
    /// auf <see cref="CoverSearchResult"/> abbilden. Die Helfer-Methode <see cref="SearchJsonAsync"/>
    /// kapselt Whitespace-Guard, GET-mit-Fehlertoleranz und die Ergebnis-Schleife; Ableitungen
    /// liefern nur URL-Aufbau, Trefferliste und Einzelabbildung.
    /// </summary>
    public abstract class JsonCoverSearchServiceBase : ICoverSearchService
    {
        /// <summary>Der vorkonfigurierte HTTP-Client (inkl. evtl. nötigem User-Agent-Header).</summary>
        protected HttpClient HttpClient { get; }

        /// <summary>Maximale Trefferzahl; Standard 9 (3×3-Grid). Ableitungen können abweichen.</summary>
        protected virtual int MaxResults => 9;

        /// <summary>
        /// Initialisiert die Basis mit einem vorkonfigurierten <see cref="HttpClient"/>.
        /// </summary>
        /// <param name="httpClient">HTTP-Client, bereitgestellt durch IHttpClientFactory.</param>
        protected JsonCoverSearchServiceBase(HttpClient httpClient)
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            HttpClient = httpClient;
        }

        /// <inheritdoc/>
        public abstract Task<IReadOnlyList<CoverSearchResult>> SearchAsync(string title, CancellationToken ct = default);

        /// <summary>
        /// Führt eine Cover-Suche gegen eine JSON-API durch: kodiert den Titel, ruft die URL ab,
        /// deserialisiert nach <typeparamref name="TResponse"/> und bildet jeden Treffer ab.
        /// Netzwerk- und Deserialisierungsfehler ergeben eine leere Liste statt einer Ausnahme.
        /// </summary>
        /// <typeparam name="TResponse">Der deserialisierte API-Antworttyp.</typeparam>
        /// <typeparam name="TItem">Der Typ eines einzelnen Treffers.</typeparam>
        /// <param name="title">Der Suchbegriff.</param>
        /// <param name="buildUrl">Baut aus kodiertem Titel und <see cref="MaxResults"/> die Anfrage-URL.</param>
        /// <param name="getItems">Liefert die Trefferliste aus der Antwort.</param>
        /// <param name="mapItem">Bildet einen Treffer ab oder liefert <c>null</c>, um ihn zu verwerfen.</param>
        /// <param name="ct">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Die abgebildeten Cover-Treffer.</returns>
        protected async Task<IReadOnlyList<CoverSearchResult>> SearchJsonAsync<TResponse, TItem>(
            string title,
            Func<string, int, string> buildUrl,
            Func<TResponse, IReadOnlyList<TItem>?> getItems,
            Func<TItem, string, CoverSearchResult?> mapItem,
            CancellationToken ct)
            where TResponse : class
        {
            ArgumentNullException.ThrowIfNull(buildUrl);
            ArgumentNullException.ThrowIfNull(getItems);
            ArgumentNullException.ThrowIfNull(mapItem);

            if (string.IsNullOrWhiteSpace(title))
            {
                return [];
            }

            TResponse? response;

            try
            {
                string encodedTitle = Uri.EscapeDataString(title);
                string url = buildUrl(encodedTitle, MaxResults);

                response = await HttpClient.GetFromJsonAsync<TResponse>(url, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransientRequestError(ex))
            {
                // Netzwerkfehler oder JSON-Deserialisierung → leere Liste, kein Absturz
                return [];
            }

            if (response is null)
            {
                return [];
            }

            IReadOnlyList<TItem>? items = getItems(response);
            if (items is not { Count: > 0 })
            {
                return [];
            }

            List<CoverSearchResult> results = [];

            foreach (TItem item in items)
            {
                CoverSearchResult? result = mapItem(item, title);
                if (result is not null)
                {
                    results.Add(result);
                }
            }

            return results;
        }

        /// <summary>
        /// Erkennt erwartbare Netzwerk- und Deserialisierungsfehler, bei denen eine leere
        /// Ergebnisliste statt einer Ausnahme geliefert wird.
        /// </summary>
        /// <param name="ex">Die aufgetretene Ausnahme.</param>
        /// <returns><c>true</c>, wenn der Fehler tolerierbar ist.</returns>
        internal static bool IsTransientRequestError(Exception ex) =>
            ex is HttpRequestException
               or TaskCanceledException
               or JsonException
               or NotSupportedException
               or UriFormatException
               or InvalidOperationException;
    }
}
