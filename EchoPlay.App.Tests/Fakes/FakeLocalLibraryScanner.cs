using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Scanning;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ILocalLibraryScanner"/>.
    /// Gibt vorab konfigurierte Scan-Ergebnisse zurück, ohne das Dateisystem zu berühren.
    /// </summary>
    internal sealed class FakeLocalLibraryScanner : ILocalLibraryScanner
    {
        private readonly IReadOnlyList<LocalScanResult> _results;
        private readonly IReadOnlyList<string> _seriesFolders;

        /// <summary>
        /// Erstellt den Fake mit festen Scan-Ergebnissen.
        /// </summary>
        /// <param name="results">Die von <see cref="ScanSeriesAsync"/> zurückzugebenden Ergebnisse.</param>
        /// <param name="seriesFolders">
        /// Die von <see cref="GetSeriesFolders"/> zurückzugebenden Ordnerpfade.
        /// Wird nicht angegeben, leitet der Fake die Pfade aus den <paramref name="results"/> ab.
        /// </param>
        public FakeLocalLibraryScanner(
            IReadOnlyList<LocalScanResult> results,
            IReadOnlyList<string>? seriesFolders = null)
        {
            _results = results;
            // Kein seriesFolders angegeben → Pfade aus den Scan-Ergebnissen ableiten
            _seriesFolders = seriesFolders ?? [];
        }

        /// <inheritdoc/>
        public IReadOnlyList<string> GetSeriesFolders(string rootPath)
        {
            return _seriesFolders;
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<LocalScanResult>> ScanSeriesAsync(
            string rootPath,
            string folderPattern,
            IProgress<ScanProgress>? progress = null,
            IProgress<LocalScanResult>? onSeriesScanned = null,
            CancellationToken ct = default)
        {
            // onSeriesScanned-Callbacks wie die echte Implementierung pro Ergebnis auslösen
            foreach (LocalScanResult result in _results)
            {
                onSeriesScanned?.Report(result);
            }

            return Task.FromResult(_results);
        }
    }
}
