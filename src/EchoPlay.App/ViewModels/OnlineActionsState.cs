using System;
using System.Collections.Generic;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Geteilter mutabler Zustand zwischen den Sub-Actions der Online-Mediathek.
    /// Der Serien-Loader befüllt die Liste abgeschlossener Episoden beim Laden;
    /// die Episoden-Pipeline liest sie beim Erzeugen der Episodenkacheln.
    /// </summary>
    internal sealed class OnlineActionsState
    {
        /// <summary>IDs aller als abgeschlossen markierten Episoden (serienübergreifend).</summary>
        public HashSet<Guid> CompletedEpisodeIds { get; set; } = [];
    }
}
