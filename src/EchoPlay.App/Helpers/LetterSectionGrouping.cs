namespace EchoPlay.App.Helpers
{
    /// <summary>
    /// Für Seiten, die auch anders als nach Namen sortieren: ob gerade nach Buchstaben gegliedert
    /// wird, und wie die ungegliederte Liste für die Sprachausgabe heißt. Bei einer Liste nach
    /// Folgenanzahl wäre eine Buchstaben-Gliederung nur noch Zufall.
    /// </summary>
    public sealed class LetterSectionGrouping
    {
        /// <summary>Legt Schalter und Beschriftung fest.</summary>
        /// <param name="isGrouped">Liefert, ob die Seite gerade nach Namen sortiert und damit gegliedert ist.</param>
        /// <param name="ungroupedAutomationName">Beschriftung der ungegliederten Liste für die Sprachausgabe.</param>
        /// <exception cref="ArgumentException">Die Beschriftung ist leer; die Sprachausgabe läse sonst den Typnamen vor.</exception>
        public LetterSectionGrouping(Func<bool> isGrouped, string ungroupedAutomationName)
        {
            ArgumentNullException.ThrowIfNull(isGrouped);
            ArgumentException.ThrowIfNullOrWhiteSpace(ungroupedAutomationName);
            IsGrouped = isGrouped;
            UngroupedAutomationName = ungroupedAutomationName;
        }

        /// <summary>Liefert, ob gerade nach Buchstaben gegliedert wird.</summary>
        public Func<bool> IsGrouped { get; }

        /// <summary>Beschriftung der ungegliederten Liste für die Sprachausgabe.</summary>
        public string UngroupedAutomationName { get; }
    }
}
