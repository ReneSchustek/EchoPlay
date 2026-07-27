using EchoPlay.App.Services;
using System.Globalization;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Baut die Automation-Namen der Kachel-Schaltflächen: Funktion plus Bezugsobjekt,
    /// z.B. „Weitere Aktionen: Die drei ??? – Folge 240".
    /// </summary>
    /// <remarks>
    /// Nötig, weil <c>x:Uid</c> innerhalb eines <c>DataTemplate</c> nicht zuverlässig greift –
    /// der Name muss also aus den Daten kommen. Ohne diese Verbindung nennt ein Screenreader
    /// entweder nur die Funktion (bei welchem Element?) oder nur den Titel (was tut der Knopf?).
    /// </remarks>
    internal static class AutomationNameFormatter
    {
        /// <summary>
        /// Setzt ein lokalisiertes Muster mit dem Bezugsobjekt zusammen.
        /// </summary>
        /// <param name="localizationService">Ressourcen-Zugriff; <see langword="null"/> in Tests.</param>
        /// <param name="resourceKey">Schlüssel des Musters, muss <c>{0}</c> enthalten.</param>
        /// <param name="fallbackPattern">Muster, falls die Ressource fehlt.</param>
        /// <param name="subject">Titel des Elements, auf das sich die Schaltfläche bezieht.</param>
        /// <returns>Der fertige Automation-Name.</returns>
        public static string Format(
            ILocalizationService? localizationService,
            string resourceKey,
            string fallbackPattern,
            string subject)
        {
            string pattern = localizationService?.Get(resourceKey) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(pattern))
            {
                pattern = fallbackPattern;
            }

            return string.Format(CultureInfo.CurrentCulture, pattern, subject);
        }
    }
}
