namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Ob eine Folge beim Bearbeiten eine Nummer tragen muss. Wer eine nummerierte Folge
    /// bearbeitet, darf die Nummer nicht versehentlich leeren; Sonderfolgen haben keine.
    /// </summary>
    public enum EpisodeNumberRule
    {
        /// <summary>Ein leeres Feld ist erlaubt.</summary>
        Optional,

        /// <summary>Das Feld darf nicht leer sein.</summary>
        Required,
    }
}
