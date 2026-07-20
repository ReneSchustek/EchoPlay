using EchoPlay.TagManager.Abstractions;
using EchoPlay.TagManager.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IFileRenameService"/>.
    /// Erstellt keine Vorschau und benennt keine Dateien um.
    /// Ausreichend für Tests die das Umbenennen nicht prüfen.
    /// </summary>
    internal sealed class FakeFileRenameService : IFileRenameService
    {
        /// <inheritdoc/>
        public IReadOnlyList<RenamePreviewItem> BuildPreview(
            IReadOnlyList<(string FilePath, AudioTag Tag)> files,
            string pattern)
            => [];

        /// <inheritdoc/>
        public Task<int> RenameAsync(
            IReadOnlyList<(string FilePath, AudioTag Tag)> files,
            string pattern)
            => Task.FromResult(0);
    }
}
