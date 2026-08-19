using EchoPlay.App.Models;
using EchoPlay.App.Services;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IFolderRestructureCoordinator"/>.
    /// Liefert eine vorgegebene Vorschau und zählt die Ausführungen.
    /// </summary>
    internal sealed class FakeFolderRestructureCoordinator : IFolderRestructureCoordinator
    {
        /// <summary>Alle analysierten Ordnerpfade in Reihenfolge.</summary>
        public List<string> AnalyzedFolders { get; } = [];

        /// <summary>Antwort auf <see cref="AnalyzeAsync"/>. <see langword="null"/> heißt „nichts zu verschieben".</summary>
        public RestructurePreviewDisplay? PreviewToReturn { get; set; }

        /// <summary>Anzahl der Ausführungen.</summary>
        public int ExecuteCallCount { get; private set; }

        /// <summary>Anzahl verschobener Dateien, die <see cref="ExecuteAsync"/> meldet.</summary>
        public int MovedFileCount { get; set; }

        /// <inheritdoc/>
        public Task<RestructurePreviewDisplay?> AnalyzeAsync(string seriesFolderPath, CancellationToken cancellationToken = default)
        {
            AnalyzedFolders.Add(seriesFolderPath);
            return Task.FromResult(PreviewToReturn);
        }

        /// <inheritdoc/>
        public Task<int> ExecuteAsync(RestructurePreviewDisplay preview, CancellationToken cancellationToken = default)
        {
            ExecuteCallCount++;
            return Task.FromResult(MovedFileCount);
        }
    }
}
