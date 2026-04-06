namespace EchoPlay.App.Services
{
    /// <summary>
    /// Stellt Zugriff auf lokalisierte UI-Strings bereit.
    /// Intern werden die <c>.resw</c>-Ressourcendateien aus <c>Strings/{sprache}/Resources.resw</c> genutzt.
    /// </summary>
    public interface ILocalizationService
    {
        /// <summary>
        /// Gibt den lokalisierten String für den angegebenen Schlüssel zurück.
        /// Ist der Schlüssel nicht vorhanden, wird ein leerer String zurückgegeben.
        /// </summary>
        /// <param name="key">Der Ressourcenschlüssel, z.B. <c>"NavStartseite.Content"</c>.</param>
        /// <returns>Der lokalisierte String oder <see cref="string.Empty"/> wenn der Schlüssel fehlt.</returns>
        string Get(string key);
    }
}
