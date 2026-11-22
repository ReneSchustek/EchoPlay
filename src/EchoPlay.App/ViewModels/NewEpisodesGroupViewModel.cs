using System.Collections.Generic;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Gruppiert Neuerscheinungen nach Zeitraum (Monat oder "Angekündigt").
    /// Im Dashboard wird pro Gruppe eine eigene Kachelreihe mit Monatsname als Überschrift angezeigt.
    /// </summary>
    /// <remarks>
    /// Reihenfolge der Gruppen im Dashboard (von oben nach unten), festgelegt durch die
    /// Reihenfolge, in der <see cref="NewReleaseGroupBuilder"/> sie anlegt:
    /// 1. "Angekündigt": Episoden mit Datum in der Zukunft
    /// 2. Aktueller Monat (z.B. "März 2026")
    /// 3. Vormonat, usw., absteigend bis zum Cutoff
    /// </remarks>
    public sealed class NewEpisodesGroupViewModel
    {
        /// <summary>
        /// Erstellt eine Monatsgruppe für Neuerscheinungen.
        /// </summary>
        /// <param name="groupLabel">Überschrift der Gruppe (z.B. "März 2026" oder "Angekündigt").</param>
        /// <param name="episodes">Die Episoden dieser Gruppe, sortiert nach Erscheinungsdatum absteigend.</param>
        public NewEpisodesGroupViewModel(string groupLabel, IReadOnlyList<NewEpisodeCardViewModel> episodes)
        {
            GroupLabel = groupLabel;
            Episodes = episodes;
        }

        /// <summary>
        /// Überschrift der Gruppe; wird als Titel über der Kachelreihe angezeigt.
        /// Beispiele: "Angekündigt", "März 2026", "Februar 2026".
        /// </summary>
        public string GroupLabel { get; }

        /// <summary>Episoden dieser Gruppe, sortiert nach Erscheinungsdatum absteigend.</summary>
        public IReadOnlyList<NewEpisodeCardViewModel> Episodes { get; }
    }
}
