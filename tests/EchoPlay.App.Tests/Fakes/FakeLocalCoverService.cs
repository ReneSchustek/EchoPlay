using EchoPlay.LocalLibrary.Cover;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ILocalCoverService"/>.
    /// Liefert standardmäßig <see langword="null"/> — der Fall „im Ordner liegt kein
    /// Cover“. Mit vorgegebenen Bilddaten lässt sich der Gegenfall prüfen: Was beim
    /// Einlesen gefunden wird, muss in der Ablage landen.
    /// </summary>
    internal sealed class FakeLocalCoverService : ILocalCoverService
    {
        private readonly byte[]? _cover;

        /// <param name="cover">Bilddaten, die jede Auflösung liefert. Ohne Angabe: kein Cover.</param>
        public FakeLocalCoverService(byte[]? cover = null) => _cover = cover;

        /// <summary>Jeder Auflösungsversuch mit seinen Argumenten, in Aufrufreihenfolge.</summary>
        public System.Collections.Generic.List<(string SeriesFolder, string? CoverImageUrl)> Calls { get; } = [];

        /// <inheritdoc/>
        public Task<byte[]?> ResolveAsync(string seriesFolder, string? coverImageUrl, CancellationToken cancellationToken = default)
        {
            Calls.Add((seriesFolder, coverImageUrl));
            return Task.FromResult(_cover);
        }
    }
}
