using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Scanning;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.LocalLibrary.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="ILocalLibraryScanner"/>.
    /// Gibt vorab konfigurierte Ergebnisse zurück, ohne das Dateisystem zu berühren.
    /// </summary>
    internal sealed class FakeLocalLibraryScanner : ILocalLibraryScanner
    {
        private readonly IReadOnlyList<LocalScanResult> _results;
        private readonly IReadOnlyList<string> _seriesFolders;

        /// <summary>Anzahl der ScanSeriesAsync-Aufrufe – für Assertions in Tests.</summary>
        public int ScanCallCount { get; private set; }

        /// <summary>Zuletzt empfangene ScanProgress-Objekte.</summary>
        public List<ScanProgress> ReportedProgress { get; } = [];

        /// <summary>
        /// Erstellt den Fake mit festen Ergebnissen.
        /// </summary>
        public FakeLocalLibraryScanner(
            IReadOnlyList<LocalScanResult>? results = null,
            IReadOnlyList<string>? seriesFolders = null)
        {
            _results = results ?? [];
            _seriesFolders = seriesFolders ?? [];
        }

        /// <inheritdoc/>
        public IReadOnlyList<string> GetSeriesFolders(string rootPath) => _seriesFolders;

        /// <inheritdoc/>
        public Task<IReadOnlyList<LocalScanResult>> ScanSeriesAsync(
            string rootPath,
            string folderPattern,
            IProgress<ScanProgress>? progress = null,
            IProgress<LocalScanResult>? onSeriesScanned = null,
            CancellationToken ct = default)
        {
            ScanCallCount++;

            foreach (LocalScanResult result in _results)
            {
                onSeriesScanned?.Report(result);
            }

            return Task.FromResult(_results);
        }
    }
}
