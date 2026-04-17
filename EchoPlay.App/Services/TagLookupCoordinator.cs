using EchoPlay.LocalLibrary.Parsing;
using EchoPlay.TagManager.Abstractions;
using EchoPlay.TagManager.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Standardimplementierung von <see cref="ITagLookupCoordinator"/>.
    /// Delegiert den eigentlichen Online-Lookup an <see cref="ITagLookupService"/>
    /// (MusicBrainz) und hält zusätzlich die reine Query-/Match-Logik, die früher
    /// als statische Methoden auf dem <c>TagManagerViewModel</c> lag.
    /// Der Coordinator ist Singleton; die <see cref="ITagLookupService"/>-Instanz wird pro
    /// Aufruf aus einem frischen DI-Scope aufgelöst, damit die HttpClient-Lifetime des
    /// <c>HttpMessageHandler</c>s nicht in der Singleton-Referenz einfriert.
    /// </summary>
    public sealed class TagLookupCoordinator : ITagLookupCoordinator
    {
        private readonly IServiceScopeFactory _scopeFactory;

        /// <summary>
        /// Initialisiert den Coordinator mit der Scope-Factory.
        /// </summary>
        /// <param name="scopeFactory">Stellt pro Lookup einen frischen DI-Scope bereit.</param>
        public TagLookupCoordinator(IServiceScopeFactory scopeFactory)
        {
            ArgumentNullException.ThrowIfNull(scopeFactory);
            _scopeFactory = scopeFactory;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<TagLookupResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ITagLookupService lookupService = scope.ServiceProvider.GetRequiredService<ITagLookupService>();
            return await lookupService.SearchAsync(query, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public string BuildAutoLookupQuery(string? folderPath) => BuildAutoLookupQueryCore(folderPath);

        /// <inheritdoc />
        public TagLookupResult? SelectBestMatch(IReadOnlyList<TagLookupResult> results, int loadedTrackCount)
            => SelectBestMatchCore(results, loadedTrackCount);

        // Die beiden Helfer sind pure Funktionen und werden auch vom TagManagerViewModel
        // als statische Shims für bestehende Unit-Tests verwendet (kein HttpClient nötig).
        internal static string BuildAutoLookupQueryCore(string? folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return string.Empty;
            }

            string seriesName = Path.GetFileName(Path.GetDirectoryName(folderPath)) ?? string.Empty;

            string episodeFolderName = Path.GetFileName(folderPath) ?? string.Empty;
            string episodeTitle = EpisodeFolderParser.StripLeadingSequenceNumber(episodeFolderName);

            if (string.IsNullOrWhiteSpace(seriesName) || string.IsNullOrWhiteSpace(episodeTitle))
            {
                return string.Empty;
            }

            return $"{seriesName} {episodeTitle}";
        }

        internal static TagLookupResult? SelectBestMatchCore(IReadOnlyList<TagLookupResult> results, int loadedTrackCount)
        {
            ArgumentNullException.ThrowIfNull(results);
            if (results.Count == 0)
            {
                return null;
            }

            TagLookupResult? exactMatch = results.FirstOrDefault(
                r => r.TrackCount.HasValue && r.TrackCount.Value == (uint)loadedTrackCount);

            return exactMatch ?? results[0];
        }
    }
}
