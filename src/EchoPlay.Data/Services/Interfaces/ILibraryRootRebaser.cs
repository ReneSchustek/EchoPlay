namespace EchoPlay.Data.Services.Interfaces
{
    /// <summary>
    /// Verschiebt die gespeicherten Pfade der Mediathek auf einen neuen Stammpfad.
    /// </summary>
    public interface ILibraryRootRebaser
    {
        /// <summary>
        /// Rechnet alle Ordner- und Spurpfade unter <paramref name="oldRoot"/> auf
        /// <paramref name="newRoot"/> um und setzt den Stammpfad in den Einstellungen neu.
        /// </summary>
        /// <param name="oldRoot">Der bisherige Stammpfad, absolut.</param>
        /// <param name="newRoot">Der neue Stammpfad, absolut.</param>
        /// <param name="cancellationToken">Bricht vor dem Speichern ab; dann bleibt alles unverändert.</param>
        /// <returns>Wie viele Pfade umgerechnet wurden und wie viele außerhalb lagen.</returns>
        Task<LibraryRebaseResult> RebaseAsync(string oldRoot, string newRoot, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Das Ergebnis einer Verschiebung des Stammpfads.
    /// </summary>
    /// <param name="Rebased">Umgerechnete Pfade.</param>
    /// <param name="OutsideRoot">Pfade außerhalb des alten Stamms, unverändert gelassen.</param>
    public sealed record LibraryRebaseResult(int Rebased, int OutsideRoot);
}
