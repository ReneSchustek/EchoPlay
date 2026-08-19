using EchoPlay.LocalLibrary.Analysis;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EchoPlay.App.Tests.Fakes
{
    /// <summary>
    /// Fake für <see cref="IEpisodePatternAnalyzer"/>.
    /// Gibt vorgegebene Vorschläge zurück – kein Dateisystemzugriff in Tests.
    /// </summary>
    internal sealed class FakeEpisodePatternAnalyzer : IEpisodePatternAnalyzer
    {
        private readonly IReadOnlyList<PatternSuggestion> _suggestions;

        /// <summary>
        /// Erstellt den Nachbau.
        /// </summary>
        /// <param name="suggestions">
        /// Was die Untersuchung liefert. Leer, wenn nicht angegeben. Die Zahl der Vorschläge
        /// und ihre Trefferquote entscheiden darüber, ob ein Muster ungefragt übernommen
        /// wird — ein Nachbau, der immer nichts findet, kann diese Weiche nicht zeigen.
        /// </param>
        public FakeEpisodePatternAnalyzer(IReadOnlyList<PatternSuggestion>? suggestions = null)
        {
            _suggestions = suggestions ?? [];
        }

        /// <summary>Der zuletzt untersuchte Ordner.</summary>
        public string? LastFolderPath { get; private set; }

        /// <inheritdoc/>
        public Task<IReadOnlyList<PatternSuggestion>> AnalyzeAsync(string seriesFolderPath)
        {
            LastFolderPath = seriesFolderPath;
            return Task.FromResult(_suggestions);
        }
    }
}
