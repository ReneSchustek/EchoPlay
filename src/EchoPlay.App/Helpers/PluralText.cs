using EchoPlay.App.Services;

namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Wählt zwischen der Singular- und der Plural-Ressource einer Mengenangabe.
    /// </summary>
    /// <remarks>
    /// Zahl plus Substantiv lässt sich nicht durch Anhängen eines Suffix bilden: Deutsch und
    /// Englisch beugen unterschiedlich, und mit der Anzahl wechselt oft auch das Verb
    /// („1 Datei wird gespeichert" gegen „2 Dateien werden gespeichert"). Jede Mengenangabe
    /// bekommt deshalb zwei Ressourcen nach dem Schema <c>&lt;Name&gt;Singular</c> und
    /// <c>&lt;Name&gt;Plural</c>.
    /// <para>
    /// Der Helfer liefert nur das Muster. Das Einsetzen der Werte bleibt an der Aufrufstelle,
    /// weil dort bekannt ist, welche Platzhalter das Muster hat und in welcher Reihenfolge.
    /// </para>
    /// </remarks>
    internal static class PluralText
    {
        /// <summary>
        /// Liefert das Muster für die Anzahl aus den Ressourcen der App.
        /// </summary>
        /// <param name="count">Anzahl, die über Singular oder Plural entscheidet.</param>
        /// <param name="singularKey">Ressourcen-Schlüssel für genau eins.</param>
        /// <param name="pluralKey">Ressourcen-Schlüssel für alle anderen Anzahlen.</param>
        /// <param name="singularFallback">Muster für genau eins, falls die Ressource fehlt.</param>
        /// <param name="pluralFallback">Muster für alle anderen Anzahlen, falls die Ressource fehlt.</param>
        /// <returns>Das Muster mit seinen Platzhaltern.</returns>
        public static string Pattern(
            int count,
            string singularKey,
            string pluralKey,
            string singularFallback,
            string pluralFallback)
            => count == 1
                ? SafeResourceLoader.Get(singularKey, singularFallback)
                : SafeResourceLoader.Get(pluralKey, pluralFallback);

        /// <summary>
        /// Liefert das Muster für die Anzahl über den injizierten Ressourcen-Dienst.
        /// </summary>
        /// <param name="localizationService">Ressourcen-Zugriff; <see langword="null"/> in Tests.</param>
        /// <param name="count">Anzahl, die über Singular oder Plural entscheidet.</param>
        /// <param name="singularKey">Ressourcen-Schlüssel für genau eins.</param>
        /// <param name="pluralKey">Ressourcen-Schlüssel für alle anderen Anzahlen.</param>
        /// <param name="singularFallback">Muster für genau eins, falls die Ressource fehlt.</param>
        /// <param name="pluralFallback">Muster für alle anderen Anzahlen, falls die Ressource fehlt.</param>
        /// <returns>Das Muster mit seinen Platzhaltern.</returns>
        public static string Pattern(
            ILocalizationService? localizationService,
            int count,
            string singularKey,
            string pluralKey,
            string singularFallback,
            string pluralFallback)
            => count == 1
                ? localizationService?.Get(singularKey) ?? singularFallback
                : localizationService?.Get(pluralKey) ?? pluralFallback;
    }
}
