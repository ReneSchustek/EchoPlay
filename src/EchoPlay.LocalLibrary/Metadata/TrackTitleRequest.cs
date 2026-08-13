namespace EchoPlay.LocalLibrary.Metadata
{
    /// <summary>
    /// Eine Spur, für die ein Anzeigename ermittelt werden soll.
    /// </summary>
    /// <param name="FilePath">Absoluter Pfad zur Audiodatei.</param>
    /// <param name="TrackNumber">
    /// Nummer, die in der Trackliste bereits in eigener Spalte steht. Sie entscheidet,
    /// ob eine gleichlautende führende Zahl aus dem Dateinamen entfernt werden darf.
    /// </param>
    public readonly record struct TrackTitleRequest(string FilePath, int TrackNumber);
}
