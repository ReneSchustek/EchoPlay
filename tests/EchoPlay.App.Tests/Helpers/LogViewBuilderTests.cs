using EchoPlay.App.Helpers;
using System.Collections.Generic;

namespace EchoPlay.App.Tests.Helpers
{
    /// <summary>
    /// Tests für den Aufbau der Protokollanzeige. Der Fehler „Protokollseite bleibt beim
    /// Öffnen leer" fiel nur am laufenden Programm auf, weil das Befüllen im Code-Behind
    /// lag; diese Entscheidung ist jetzt ohne Oberfläche prüfbar.
    /// </summary>
    public sealed class LogViewBuilderTests
    {
        [Fact]
        public void BuildLines_WithEntries_ReturnsThemUnchanged()
        {
            List<string> entries = ["10:00 INFO Start", "10:01 WARN Platte fast voll"];

            IReadOnlyList<string> lines = LogViewBuilder.BuildLines(entries, isViewerAvailable: true);

            Assert.Equal(entries, lines);
        }

        [Fact]
        public void BuildLines_WithoutEntries_ReturnsHintInsteadOfNothing()
        {
            IReadOnlyList<string> lines = LogViewBuilder.BuildLines([], isViewerAvailable: true);

            string einzige = Assert.Single(lines);
            Assert.Equal("Keine Protokolleinträge vorhanden.", einzige);
        }

        [Fact]
        public void BuildLines_WithNullEntries_ReturnsHint()
        {
            IReadOnlyList<string> lines = LogViewBuilder.BuildLines(null, isViewerAvailable: true);

            _ = Assert.Single(lines);
        }

        [Fact]
        public void BuildLines_WithoutViewer_ExplainsWhyNothingIsShown()
        {
            // Ohne registrierten Puffer kann die Anzeige nie etwas enthalten – der
            // Hinweis unterscheidet diesen Zustand von „nichts passiert".
            IReadOnlyList<string> lines = LogViewBuilder.BuildLines(
                ["10:00 INFO Start"], isViewerAvailable: false);

            string einzige = Assert.Single(lines);
            Assert.Contains("nicht verfügbar", einzige, System.StringComparison.Ordinal);
        }
    }
}
