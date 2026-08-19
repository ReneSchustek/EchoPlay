using EchoPlay.TagManager.Abstractions;
using EchoPlay.TagManager.Models;
using System;
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
        /// <param name="readFolderFailure">
        /// Wird von <see cref="ReadFolderAsync"/> geworfen, wenn gesetzt. Ein defekter
        /// Kennzeichnungsblock in einer einzigen Datei bringt den echten Dienst zum Werfen —
        /// ohne diesen Fall bliebe der Behandlungszweig ungeprüft.
        /// </param>
        public FakeTagService(
            IReadOnlyList<(string, AudioTag)>? folderFiles = null,
            Exception? readFolderFailure = null)
        {
            _folderFiles = folderFiles ?? [];
            _readFolderFailure = readFolderFailure;
        }

        private readonly Exception? _readFolderFailure;

        /// <summary>
        /// Ist der Wert gesetzt, scheitert jedes Lesen damit. Eine beschädigte Datei ist
        /// der Fall, den der Anwender als Meldung sehen muss.
        /// </summary>
        public Exception? ReadFailure { get; set; }

        /// <inheritdoc/>
        public Task<AudioTag> ReadAsync(string filePath)
        {
            if (ReadFailure is not null)
            {
                return Task.FromException<AudioTag>(ReadFailure);
            }

            // Zuerst geschriebene Tags zurückgeben, dann aus der Ordnerliste, dann leer
            if (_writtenTags.TryGetValue(filePath, out AudioTag? written))
            {
                return Task.FromResult(written);
            }

            (string _, AudioTag tag) = _folderFiles.FirstOrDefault(f => f.FilePath == filePath);
            return Task.FromResult(tag ?? new AudioTag());
        }

        /// <summary>
        /// Wird gesetzt, scheitert jedes Schreiben damit. Eine schreibgeschützte oder
        /// beschädigte Datei ist der Fall, den der Anwender gemeldet bekommen muss.
        /// </summary>
        public Exception? WriteFailure { get; set; }

        /// <inheritdoc/>
        public Task WriteAsync(string filePath, AudioTag tag)
        {
            WriteCallCount++;

            if (WriteFailure is not null)
            {
                return Task.FromException(WriteFailure);
            }

            _writtenTags[filePath] = tag;
            return Task.CompletedTask;
        }

        /// <summary>Wird gesetzt, scheitert jedes Schreiben eines Covers damit.</summary>
        public Exception? WriteCoverFailure { get; set; }

        /// <inheritdoc/>
        public Task WriteCoverAsync(string filePath, byte[]? imageData, string mimeType = "image/jpeg")
        {
            WriteCoverCallCount++;

            if (WriteCoverFailure is not null)
            {
                return Task.FromException(WriteCoverFailure);
            }

            _writtenCovers[filePath] = imageData;
            return Task.CompletedTask;
        }

        /// <summary>Wie oft alle Angaben einer Datei entfernt wurden.</summary>
        public int RemoveAllCallCount { get; private set; }

        /// <summary>Wird gesetzt, scheitert das Entfernen aller Angaben damit.</summary>
        public Exception? RemoveAllFailure { get; set; }

        /// <inheritdoc/>
        public Task RemoveAllTagsAsync(string filePath)
        {
            RemoveAllCallCount++;
            return RemoveAllFailure is null ? Task.CompletedTask : Task.FromException(RemoveAllFailure);
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<(string FilePath, AudioTag Tag)>> ReadFolderAsync(string folderPath)
            => _readFolderFailure is not null
                ? Task.FromException<IReadOnlyList<(string FilePath, AudioTag Tag)>>(_readFolderFailure)
                : Task.FromResult(_folderFiles);

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
