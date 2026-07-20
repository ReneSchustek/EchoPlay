using EchoPlay.LocalLibrary.Analysis;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IEpisodePatternAnalyzer"/>.
    /// Gibt immer eine leere Liste zurück – kein Dateisystemzugriff in Tests.
    /// </summary>
    internal sealed class FakeEpisodePatternAnalyzer : IEpisodePatternAnalyzer
    {
        /// <inheritdoc/>
        public Task<IReadOnlyList<PatternSuggestion>> AnalyzeAsync(string seriesFolderPath)
        {
            return Task.FromResult<IReadOnlyList<PatternSuggestion>>([]);
        }
    }
}
