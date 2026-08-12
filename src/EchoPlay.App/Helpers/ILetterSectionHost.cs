using System.Collections.Generic;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Die Stelle, an der eine Abschnittsliste angezeigt wird.
    /// <para>
    /// Die Schnittstelle ist schmal, damit die Frage „welcher Abschnitt gehört wohin, und wo
    /// landet ein Sprung" ohne laufende Oberfläche prüfbar bleibt. Übrig bleibt für WinUI nur
    /// das Zuweisen der Liste und das Scrollen — beides ohne eigene Entscheidung.
    /// </para>
    /// </summary>
    public interface ILetterSectionHost
    {
        /// <summary>Die angezeigten Abschnitte.</summary>
        IReadOnlyList<LetterSection> Sections { get; set; }

        /// <summary>
        /// Holt einen Abschnitt ins Blickfeld.
        /// </summary>
        /// <param name="section">Der anzuzeigende Abschnitt.</param>
        /// <returns><see langword="true"/>, wenn er erreicht wurde.</returns>
        bool TryBringIntoView(LetterSection section);
    }
}
