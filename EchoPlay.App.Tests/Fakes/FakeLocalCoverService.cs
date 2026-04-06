using EchoPlay.LocalLibrary.Cover;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ILocalCoverService"/>.
    /// Liefert stets <see langword="null"/> zurück, da Cover-Auflösung
    /// in SyncService-Tests nicht geprüft wird und kein Dateisystem benötigt.
    /// </summary>
    internal sealed class FakeLocalCoverService : ILocalCoverService
    {
        /// <inheritdoc/>
        public Task<byte[]?> ResolveAsync(string seriesFolder, string? coverImageUrl)
        {
            return Task.FromResult<byte[]?>(null);
        }
    }
}
