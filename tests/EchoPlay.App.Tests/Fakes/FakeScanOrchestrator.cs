using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Scanning;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IScanOrchestrator"/>.
    /// Delegiert den Scan direkt an den übergebenen <see cref="ILocalLibraryScanner"/>,
    /// ohne die Phasen-Meldungen des echten Orchestrators zu erzeugen.
    /// Dadurch können SyncService-Tests dieselbe <see cref="FakeLocalLibraryScanner"/>-Instanz
    /// für beide Interfaces nutzen.
    /// </summary>
    internal sealed class FakeScanOrchestrator : IScanOrchestrator
    {
        private readonly ILocalLibraryScanner _scanner;

        /// <summary>
        /// Erstellt den Fake mit dem zu delegierenden Scanner.
        /// </summary>
        /// <param name="scanner">Scanner, dessen Ergebnisse direkt weitergegeben werden.</param>
        public FakeScanOrchestrator(ILocalLibraryScanner scanner)
        {
            _scanner = scanner;
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<LocalScanResult>> ScanAsync(
            string rootPath,
            string episodeFolderPattern,
            IProgress<ScanProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            // Phasen-Meldungen werden nicht simuliert – für SyncService-Tests irrelevant
            return _scanner.ScanSeriesAsync(rootPath, episodeFolderPattern, progress, onSeriesScanned: null, cancellationToken);
        }
    }
}
