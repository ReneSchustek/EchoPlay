using EchoPlay.App.Services;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ICoverDownloader"/>. Liefert je URL feste Bytes und protokolliert
    /// die angefragten URLs. Ohne Eintrag kommt <see langword="null"/> zurück — das ist der
    /// Fall „Download fehlgeschlagen", auf den die Aufrufer reagieren müssen.
    /// </summary>
    internal sealed class FakeCoverDownloader : ICoverDownloader
    {
        private readonly Dictionary<string, byte[]?> _responses;

        /// <summary>
        /// Erstellt den Fake.
        /// </summary>
        /// <param name="responses">Antwort je URL. Fehlt eine URL, liefert der Download null.</param>
        public FakeCoverDownloader(IReadOnlyDictionary<string, byte[]?>? responses = null)
        {
            _responses = responses is null
                ? []
                : new Dictionary<string, byte[]?>(responses);
        }

        /// <summary>Alle angefragten URLs in Aufrufreihenfolge.</summary>
        public List<string> RequestedUrls { get; } = [];

        /// <summary>Legt die Antwort für eine URL fest.</summary>
        /// <param name="url">Die URL, für die geantwortet werden soll.</param>
        /// <param name="bytes">Die Antwort-Bytes, oder null für „fehlgeschlagen".</param>
        public void SetResponse(string url, byte[]? bytes) => _responses[url] = bytes;

        /// <inheritdoc/>
        public Task<byte[]?> DownloadAsync(string url, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedUrls.Add(url);

            return Task.FromResult(_responses.TryGetValue(url, out byte[]? bytes) ? bytes : null);
        }
    }
}
