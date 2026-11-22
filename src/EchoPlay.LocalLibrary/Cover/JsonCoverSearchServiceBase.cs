using EchoPlay.Core.Http;
using EchoPlay.Logger.Abstractions;
using System.Net.Http.Json;

namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Gemeinsame Basis für Cover-Such-Services, die eine JSON-API abfragen und die Treffer
    /// auf <see cref="CoverSearchResult"/> abbilden. <see cref="SearchJsonAsync"/> übernimmt
    /// die Prüfung auf leere Suchbegriffe, die fehlertolerante Abfrage und das Blättern;
    /// Ableitungen liefern nur Adresse, Trefferliste und Einzelabbildung.
    /// </summary>
    public abstract class JsonCoverSearchServiceBase : ICoverSearchService
    {
        /// <summary>Der vorkonfigurierte HTTP-Client, wo nötig mit User-Agent-Header.</summary>
        protected HttpClient HttpClient { get; }

        /// <summary>
        /// Protokolliert übersprungene Anfragen. Transportfehler meldet schon der HTTP-Handler;
        /// hier geht es vor allem um Antworten, die sich nicht lesen lassen.
        /// </summary>
        protected ILogger Logger { get; }

        /// <summary>Treffer je Seite; neun füllen das 3×3-Raster des Auswahldialogs.</summary>
        protected virtual int MaxResults => 9;

        /// <summary>
        /// Initialisiert die Basis mit einem vorkonfigurierten <see cref="HttpClient"/>.
        /// </summary>
        /// <param name="httpClient">HTTP-Client, bereitgestellt durch IHttpClientFactory.</param>
        /// <param name="loggerFactory">Für das Protokoll übersprungener Anfragen.</param>
        protected JsonCoverSearchServiceBase(HttpClient httpClient, ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            ArgumentNullException.ThrowIfNull(loggerFactory);
            HttpClient = httpClient;
            Logger = loggerFactory.CreateLogger(GetType().Name);
        }

        /// <summary>
        /// Kennt die API einen Versatz? MusicBrainz (<c>offset</c>) und Deezer (<c>index</c>)
        /// tun es, die iTunes Search API nicht. Dort wird mehr geholt und der bereits gezeigte
        /// Anfang verworfen. Ableitungen ohne Versatz überschreiben das mit <c>false</c>.
        /// </summary>
        protected virtual bool SupportsOffset => true;

        /// <inheritdoc/>
        public Task<IReadOnlyList<CoverSearchResult>> SearchAsync(string title, CancellationToken ct = default) =>
            SearchAsync(title, CoverSearchPage.First, ct);

        /// <inheritdoc/>
        public abstract Task<IReadOnlyList<CoverSearchResult>> SearchAsync(string title, CoverSearchPage page, CancellationToken ct = default);

        /// <summary>
        /// Führt eine Cover-Suche gegen eine JSON-API durch: kodiert den Titel, ruft die URL ab,
        /// deserialisiert nach <typeparamref name="TResponse"/> und bildet jeden Treffer ab.
        /// Netzwerk- und Deserialisierungsfehler ergeben eine leere Liste statt einer Ausnahme.
        /// </summary>
        /// <typeparam name="TResponse">Der deserialisierte API-Antworttyp.</typeparam>
        /// <typeparam name="TItem">Der Typ eines einzelnen Treffers.</typeparam>
        /// <param name="title">Der Suchbegriff.</param>
        /// <param name="page">Welcher Abschnitt der Trefferliste geholt wird.</param>
        /// <param name="buildUrl">Baut aus kodiertem Titel und Abschnitt die Anfrage-URL.</param>
        /// <param name="getItems">Liefert die Trefferliste aus der Antwort.</param>
        /// <param name="mapItem">Bildet einen Treffer ab oder liefert <c>null</c>, um ihn zu verwerfen.</param>
        /// <param name="ct">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Die abgebildeten Cover-Treffer.</returns>
        protected async Task<IReadOnlyList<CoverSearchResult>> SearchJsonAsync<TResponse, TItem>(
            string title,
            CoverSearchPage page,
            Func<string, CoverSearchWindow, string> buildUrl,
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

            CoverSearchWindow window = page.ToWindow(MaxResults, SupportsOffset);
            TResponse? response = await FetchAsync<TResponse>(buildUrl(Uri.EscapeDataString(title), window), ct).ConfigureAwait(false);
            IReadOnlyList<TItem>? items = response is null ? null : getItems(response);
            if (items is not { Count: > 0 })
            {
                return [];
            }

            // Bei APIs ohne Versatz enthält die Antwort die schon gezeigten Treffer erneut;
            // die fallen hier weg. Übersprungen wird vor dem Abbilden, damit verworfene Treffer
            // (kein Bild hinterlegt) die Zählung nicht verschieben.
            return [.. items
                .Skip(window.SkipLocally)
                .Select(item => mapItem(item, title))
                .Where(result => result is not null)
                .Select(result => result!)];
        }

        /// <summary>Fragt den Anbieter und liest die Antwort.</summary>
        /// <returns>Die Antwort, oder leer, wenn der Anbieter nicht oder unlesbar geantwortet hat.</returns>
        private async Task<TResponse?> FetchAsync<TResponse>(string url, CancellationToken ct)
            where TResponse : class
        {
            try
            {
                return await HttpClient.GetFromJsonAsync<TResponse>(url, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (TransientRequestError.IsTransient(ex, ct))
            {
                // Ein Anbieter, der nicht antwortet oder Unlesbares schickt, fällt für diese Suche
                // aus; die anderen liefern weiter. Ein Abbruch des Aufrufers geht dagegen durch.
                Logger.Warning("Cover-Suche übersprungen: {Reason}", ex.Message);
                return null;
            }
        }
    }
}
