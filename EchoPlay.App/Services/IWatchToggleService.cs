using System;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Schaltet den „Auf Neuerscheinungen überwachen"-Status einer Serie um und
    /// hält den Cache der gemerkten Neuerscheinungen konsistent.
    /// Bündelt eine Logik, die sonst in beiden Mediathek-Pages dupliziert wäre.
    /// </summary>
    public interface IWatchToggleService
    {
        /// <summary>
        /// Setzt den Watch-Status einer Serie. Bei Aktivierung wird unmittelbar ein
        /// iTunes-Check für diese Serie ausgelöst, damit Neuerscheinungen beim nächsten
        /// Dashboard-Besuch bereitstehen. Bei Deaktivierung werden die Cache-Einträge entfernt.
        /// </summary>
        /// <param name="seriesId">ID der Serie, deren Watch-Status geändert werden soll.</param>
        /// <param name="watch">Neuer Status: <see langword="true"/> aktiviert die Überwachung.</param>
        /// <returns>Asynchrone Ausführung.</returns>
        Task ToggleAsync(Guid seriesId, bool watch);
    }
}
