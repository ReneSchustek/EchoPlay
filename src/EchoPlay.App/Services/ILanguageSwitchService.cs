using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Kapselt den Sprachwechsel der Oberfläche an einer Stelle – Einstellungsseite und
    /// Statusleiste teilen sich denselben Weg.
    /// </summary>
    /// <remarks>
    /// WinUI 3 lädt <c>.resw</c>-Ressourcen nur beim Start; ein Wechsel zur Laufzeit ist nicht
    /// möglich. Der Ablauf ist deshalb dreigeteilt: Sprache merken (<see cref="ApplyOverride"/>),
    /// in den Einstellungen persistieren und die App neu starten
    /// (<see cref="ChangeLanguageAsync"/>) sowie den gemerkten Wert beim nächsten Start erneut
    /// setzen (<see cref="ApplyOverride"/> aus dem Startpfad).
    /// </remarks>
    public interface ILanguageSwitchService
    {
        /// <summary>
        /// Setzt die Sprachpräferenz für den laufenden Prozess. Muss beim Start vor dem Laden
        /// der ersten Seite aufgerufen werden, sonst greifen die Ressourcen der alten Sprache.
        /// </summary>
        /// <param name="languageCode">BCP-47-Code, z.B. <c>de</c> oder <c>en</c>.</param>
        /// <returns><see langword="true"/>, wenn die Präferenz gesetzt werden konnte.</returns>
        bool ApplyOverride(string languageCode);

        /// <summary>
        /// Persistiert die Sprache in den Einstellungen und startet die App neu.
        /// </summary>
        /// <param name="languageCode">BCP-47-Code der gewünschten Sprache.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns><see langword="true"/>, wenn der Neustart angestoßen wurde.</returns>
        Task<bool> ChangeLanguageAsync(string languageCode, CancellationToken cancellationToken = default);
    }
}
