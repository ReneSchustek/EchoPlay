namespace EchoPlay.App.Services
{
    /// <summary>
    /// Stellt Zugriff auf lokalisierte UI-Strings bereit.
    /// Intern werden die <c>.resw</c>-Ressourcendateien aus <c>Strings/{sprache}/Resources.resw</c> genutzt.
    /// </summary>
    public interface ILocalizationService
    {
        /// <summary>
        /// Der Text zum Schlüssel in der eingestellten Sprache. Fehlt der Schlüssel in den Ressourcen,
        /// kommt ein sichtbarer Marker zurück (<c>[!]Schlüssel</c>), damit die Lücke in der Oberfläche
        /// auffällt; ein leerer Schlüssel ergibt einen leeren Text.
        /// </summary>
        /// <param name="key">Der Ressourcenschlüssel, z.B. <c>"NavDashboard.Content"</c>.</param>
        /// <returns>Der lokalisierte Text, der Fehlmarker oder <see cref="string.Empty"/>.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Die Regel schützt Aufrufer in Visual Basic vor Schlüsselwörtern; EchoPlay wird nur aus C# aufgerufen. Get folgt ResourceLoader.GetString und IStringLocalizer.")]
        string Get(string key);
    }
}
