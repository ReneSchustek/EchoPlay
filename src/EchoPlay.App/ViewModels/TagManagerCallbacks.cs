namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Die Rückrufe, mit denen die Aktionen den Zustand des Tag-Managers setzen. Jeder Rückruf
    /// tut ohne Angabe nichts; so nennt ein Aufrufer nur die, auf die es ihm ankommt.
    /// </summary>
    internal sealed class TagManagerCallbacks
    {
        /// <summary>Schaltet die Ladeanzeige.</summary>
        public Action<bool> SetIsLoading { get; init; } = _ => { };

        /// <summary>Schaltet die Anzeige der laufenden Abfrage.</summary>
        public Action<bool> SetIsLookingUp { get; init; } = _ => { };

        /// <summary>Setzt den Statustext der automatischen Abfrage.</summary>
        public Action<string> SetAutoLookupStatus { get; init; } = _ => { };

        /// <summary>Setzt den Fortschrittstext.</summary>
        public Action<string> SetBatchProgress { get; init; } = _ => { };

        /// <summary>Setzt die Markierung ungespeicherter Änderungen.</summary>
        public Action<bool> SetHasUnsavedChanges { get; init; } = _ => { };

        /// <summary>Aktualisiert die Befehle.</summary>
        public Action RefreshCommandStates { get; init; } = () => { };
    }
}
