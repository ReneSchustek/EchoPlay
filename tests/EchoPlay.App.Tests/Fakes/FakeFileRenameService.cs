using EchoPlay.TagManager.Abstractions;
using EchoPlay.TagManager.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IFileRenameService"/>.
    /// Liefert eine vorgegebene Vorschau und eine vorgegebene Zahl umbenannter Dateien.
    /// </summary>
    /// <remarks>
    /// Die Zahl ist einstellbar, weil der Teilerfolg ein eigener Weg ist: Wenn weniger
    /// Dateien umbenannt wurden als angekündigt, muss die Anwendung das melden. Ein Nachbau,
    /// der immer null zurückgibt, kann diesen Unterschied nicht zeigen.
    /// </remarks>
    internal sealed class FakeFileRenameService : IFileRenameService
    {
        private readonly IReadOnlyList<RenamePreviewItem> _preview;
        private readonly int _renamedCount;
        private readonly Exception? _renameFailure;

        /// <summary>Wie oft eine Vorschau angefordert wurde.</summary>
        public int BuildPreviewCallCount { get; private set; }

        /// <summary>Wie oft umbenannt wurde.</summary>
        public int RenameCallCount { get; private set; }

        /// <summary>Das zuletzt übergebene Muster.</summary>
        public string? LastPattern { get; private set; }

        /// <summary>
        /// Erstellt den Nachbau.
        /// </summary>
        /// <param name="preview">Was <see cref="BuildPreview"/> liefert. Leer, wenn nicht angegeben.</param>
        /// <param name="renamedCount">Was <see cref="RenameAsync"/> als Anzahl meldet.</param>
        /// <param name="renameFailure">Wird beim Umbenennen geworfen, wenn gesetzt.</param>
        public FakeFileRenameService(
            IReadOnlyList<RenamePreviewItem>? preview = null,
            int renamedCount = 0,
            Exception? renameFailure = null)
        {
            _preview = preview ?? [];
            _renamedCount = renamedCount;
            _renameFailure = renameFailure;
        }

        /// <inheritdoc/>
        public IReadOnlyList<RenamePreviewItem> BuildPreview(
            IReadOnlyList<(string FilePath, AudioTag Tag)> files,
            string pattern)
        {
            BuildPreviewCallCount++;
            LastPattern = pattern;
            return _preview;
        }

        /// <inheritdoc/>
        public Task<int> RenameAsync(
            IReadOnlyList<(string FilePath, AudioTag Tag)> files,
            string pattern)
        {
            RenameCallCount++;
            LastPattern = pattern;

            return _renameFailure is not null
                ? Task.FromException<int>(_renameFailure)
                : Task.FromResult(_renamedCount);
        }
    }
}
