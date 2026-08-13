using EchoPlay.Core.Parsing;
using EchoPlay.LocalLibrary.Metadata;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ITrackTitleResolver"/>.
    /// Liefert die hinterlegten Titel je Dateipfad; ohne Hinterlegung greift dieselbe
    /// Rückfallebene wie in der Anwendung — der aufgeräumte Dateiname.
    /// </summary>
    internal sealed class FakeTrackTitleResolver : ITrackTitleResolver
    {
        /// <summary>Titel je Dateipfad. Nicht eingetragene Pfade fallen auf den Dateinamen zurück.</summary>
        public Dictionary<string, string> TitlesByPath { get; } = [];

        /// <summary>Die zuletzt angefragten Spuren.</summary>
        public List<TrackTitleRequest> Requests { get; } = [];

        /// <inheritdoc/>
        public Task<IReadOnlyList<string>> ResolveAsync(
            IReadOnlyList<TrackTitleRequest> tracks,
            CancellationToken cancellationToken = default)
        {
            Requests.Clear();
            Requests.AddRange(tracks);

            List<string> titles = new(tracks.Count);

            foreach (TrackTitleRequest track in tracks)
            {
                string? tagTitle = TitlesByPath.TryGetValue(track.FilePath, out string? found) ? found : null;
                titles.Add(TrackDisplayTitle.Choose(tagTitle, track.FilePath, track.TrackNumber));
            }

            return Task.FromResult<IReadOnlyList<string>>(titles);
        }
    }
}
