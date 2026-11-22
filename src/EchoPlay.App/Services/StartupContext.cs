using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Settings;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Was alle Schritte des Startlaufs brauchen: die Einstellungen, die abonnierten
    /// Serien, die Altersgrenze für Neuerscheinungen und die Frage, ob der
    /// Zwischenspeicher geleert werden soll.
    /// </summary>
    /// <param name="Settings">Die gespeicherten Einstellungen.</param>
    /// <param name="SubscribedSeries">Alle abonnierten Serien.</param>
    /// <param name="CutoffDate">Älteres gilt nicht mehr als Neuerscheinung.</param>
    /// <param name="CacheCleared">
    /// Ob der Zwischenspeicher in diesem Lauf geleert wird. Das Merkmal wird erst ganz
    /// am Ende zurückgesetzt. Scheitert der Neuaufbau, versucht es der nächste Start
    /// erneut.
    /// </param>
    internal sealed record StartupContext(
        AppSettings Settings,
        IReadOnlyList<Series> SubscribedSeries,
        DateTime CutoffDate,
        bool CacheCleared);
}
