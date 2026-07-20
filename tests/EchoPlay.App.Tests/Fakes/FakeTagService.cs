using EchoPlay.TagManager.Abstractions;
using EchoPlay.TagManager.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ITagService"/>.
    /// Speichert geschriebene Tags im Speicher und gibt sie bei <see cref="ReadAsync"/> zurück.
    /// Für Tests die keine echten Audiodateien benötigen.
    /// </summary>
    internal sealed class FakeTagService : ITagService
    {
        private readonly IReadOnlyList<(string FilePath, AudioTag Tag)> _folderFiles;
        private readonly Dictionary<string, AudioTag> _writtenTags = [];
        private readonly Dictionary<string, byte[]?> _writtenCovers = [];

        /// <summary>Anzahl der Aufrufe von <see cref="WriteAsync"/>.</summary>
        public int WriteCallCount { get; private set; }

        /// <summary>Anzahl der Aufrufe von <see cref="WriteCoverAsync"/>.</summary>
        public int WriteCoverCallCount { get; private set; }

        /// <summary>
        /// Erstellt den Fake mit optionaler Dateiliste für <see cref="ReadFolderAsync"/>.
        /// </summary>
        /// <param name="folderFiles">
        /// Dateien die von <see cref="ReadFolderAsync"/> zurückgegeben werden.
        /// Leer wenn nicht angegeben.
        /// </param>
        public FakeTagService(IReadOnlyList<(string, AudioTag)>? folderFiles = null)
        {
            _folderFiles = folderFiles ?? [];
        }

        /// <inheritdoc/>
        public Task<AudioTag> ReadAsync(string filePath)
        {
            // Zuerst geschriebene Tags zurückgeben, dann aus der Ordnerliste, dann leer
            if (_writtenTags.TryGetValue(filePath, out AudioTag? written))
            {
                return Task.FromResult(written);
            }

            (string _, AudioTag tag) = _folderFiles.FirstOrDefault(f => f.FilePath == filePath);
            return Task.FromResult(tag ?? new AudioTag());
        }

        /// <inheritdoc/>
        public Task WriteAsync(string filePath, AudioTag tag)
        {
            WriteCallCount++;
            _writtenTags[filePath] = tag;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task WriteCoverAsync(string filePath, byte[]? imageData, string mimeType = "image/jpeg")
        {
            WriteCoverCallCount++;
            _writtenCovers[filePath] = imageData;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task RemoveAllTagsAsync(string filePath)
            => Task.CompletedTask;

        /// <inheritdoc/>
        public Task<IReadOnlyList<(string FilePath, AudioTag Tag)>> ReadFolderAsync(string folderPath)
            => Task.FromResult(_folderFiles);

        /// <summary>
        /// Gibt die zuletzt für den Pfad geschriebenen Tags zurück (für Assertions).
        /// </summary>
        public AudioTag? GetWrittenTag(string filePath)
            => _writtenTags.GetValueOrDefault(filePath);

        /// <summary>
        /// Gibt die zuletzt für den Pfad geschriebenen Cover-Daten zurück (für Assertions).
        /// </summary>
        public byte[]? GetWrittenCover(string filePath)
            => _writtenCovers.GetValueOrDefault(filePath);
    }
}
