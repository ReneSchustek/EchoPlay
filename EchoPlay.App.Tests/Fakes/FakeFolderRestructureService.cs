using EchoPlay.LocalLibrary.Abstractions;
using EchoPlay.LocalLibrary.Models;
using System.Collections.Generic;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IFolderRestructureService"/>. Liefert eine konfigurierbare
    /// Vorschau zurück und merkt sich die Aufruf-Parameter, damit Tests sowohl die
    /// Übergabe an den Service als auch die Verarbeitung der Antwort prüfen können.
    /// </summary>
    internal sealed class FakeFolderRestructureService : IFolderRestructureService
    {
        private RestructurePreview? _previewToReturn;
        private int _executeReturnValue;

        /// <summary>Letzter an <see cref="Analyze"/> übergebener Serienordner.</summary>
        public string? LastAnalyzedFolderPath { get; private set; }

        /// <summary>Letztes an <see cref="Analyze"/> übergebenes Ordnermuster.</summary>
        public string? LastFolderPattern { get; private set; }

        /// <summary>Anzahl der <see cref="Execute"/>-Aufrufe.</summary>
        public int ExecuteCallCount { get; private set; }

        /// <summary>
        /// Konfiguriert die Vorschau, die <see cref="Analyze"/> zurückgeben soll.
        /// </summary>
        public void SetAnalyzeResult(RestructurePreview preview)
        {
            _previewToReturn = preview;
        }

        /// <summary>
        /// Konfiguriert den Rückgabewert von <see cref="Execute"/>.
        /// </summary>
        public void SetExecuteResult(int filesMoved)
        {
            _executeReturnValue = filesMoved;
        }

        /// <inheritdoc/>
        public RestructurePreview Analyze(string seriesFolderPath, string folderPattern)
        {
            LastAnalyzedFolderPath = seriesFolderPath;
            LastFolderPattern = folderPattern;

            return _previewToReturn ?? new RestructurePreview
            {
                SeriesFolderPath = seriesFolderPath,
                Actions = new List<RestructureAction>()
            };
        }

        /// <inheritdoc/>
        public int Execute(RestructurePreview preview)
        {
            ExecuteCallCount++;
            return _executeReturnValue;
        }
    }
}
