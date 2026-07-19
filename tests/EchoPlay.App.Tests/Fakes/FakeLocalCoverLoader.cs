using EchoPlay.LocalLibrary.Cover;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ILocalCoverLoader"/>.
    /// Gibt immer <see langword="null"/> zurück, damit in Tests keine Bilddaten benötigt werden.
    /// </summary>
    internal sealed class FakeLocalCoverLoader : ILocalCoverLoader
    {
        /// <inheritdoc/>
        public Task<byte[]?> LoadAsync(string? episodeFolderPath, string? firstTrackPath)
        {
            return Task.FromResult<byte[]?>(null);
        }
    }
}
