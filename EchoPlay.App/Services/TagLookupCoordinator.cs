using EchoPlay.LocalLibrary.Parsing;
using EchoPlay.TagManager.Abstractions;
using EchoPlay.TagManager.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Standardimplementierung von <see cref="ITagLookupCoordinator"/>.
    /// Delegiert den eigentlichen Online-Lookup an <see cref="ITagLookupService"/>
    /// (MusicBrainz) und hält zusätzlich die reine Query-/Match-Logik, die früher
    /// als statische Methoden auf dem <c>TagManagerViewModel</c> lag.
    /// </summary>
    public sealed class TagLookupCoordinator : ITagLookupCoordinator
    {
        private readonly ITagLookupService _lookupService;

        /// <summary>
        /// Initialisiert den Coordinator mit dem Tag-Lookup-Service.
        /// </summary>
        /// <param name="lookupService">MusicBrainz-Suche (oder Fake in Tests).</param>
        public TagLookupCoordinator(ITagLookupService lookupService)
        {
            _lookupService = lookupService;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<TagLookupResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
            => _lookupService.SearchAsync(query, cancellationToken);

        /// <inheritdoc />
        public string BuildAutoLookupQuery(string? folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return string.Empty;
            }

            // Serienname aus dem übergeordneten Ordner
            string seriesName = Path.GetFileName(Path.GetDirectoryName(folderPath)) ?? string.Empty;

            // Folgentitel aus dem Ordnernamen – führende Laufnummer entfernen
            // (z.B. "001 - Der Super-Papagei" → "Der Super-Papagei")
            string episodeFolderName = Path.GetFileName(folderPath) ?? string.Empty;
            string episodeTitle      = EpisodeFolderParser.StripLeadingSequenceNumber(episodeFolderName);

            if (string.IsNullOrWhiteSpace(seriesName) || string.IsNullOrWhiteSpace(episodeTitle))
            {
                return string.Empty;
            }

            return $"{seriesName} {episodeTitle}";
        }

        /// <inheritdoc />
        public TagLookupResult? SelectBestMatch(IReadOnlyList<TagLookupResult> results, int loadedTrackCount)
        {
            if (results.Count == 0)
            {
                return null;
            }

            // Exakter Track-Count-Treffer bevorzugen
            TagLookupResult? exactMatch = results.FirstOrDefault(
                r => r.TrackCount.HasValue && r.TrackCount.Value == (uint)loadedTrackCount);

            return exactMatch ?? results[0];
        }
    }
}
