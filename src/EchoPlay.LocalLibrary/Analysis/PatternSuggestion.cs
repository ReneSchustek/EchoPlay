namespace EchoPlay.LocalLibrary.Analysis
{
    /// <summary>
    /// Ergebnis einer Mustererkennung für Episodenordner.
    /// Enthält das erkannte Muster sowie Metriken darüber, wie viele Ordnernamen damit übereinstimmen.
    /// </summary>
    /// <param name="Pattern">Das Ordnernamens-Muster, z.B. <c>{number:000} - {title}</c>.</param>
    /// <param name="MatchCount">Anzahl der Ordner- oder Dateinamen, die mit dem Muster übereinstimmen.</param>
    /// <param name="MatchPercentage">Trefferquote von 0.0 bis 1.0 bezogen auf alle analysierten Namen.</param>
    /// <param name="IsFlatStructure">
    /// Gibt an, ob dieses Muster aus Dateinamen einer flachen Struktur (MP3s direkt im Serienordner)
    /// erkannt wurde. Bei <see langword="false"/> stammt das Muster aus Episodenordnern.
    /// </param>
    public sealed record PatternSuggestion(
        string Pattern,
        int MatchCount,
        double MatchPercentage,
        bool IsFlatStructure = false);
}
