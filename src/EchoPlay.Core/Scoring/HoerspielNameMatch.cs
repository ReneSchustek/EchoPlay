namespace EchoPlay.Core.Scoring
{
    /// <summary>
    /// Was der Vergleich von Künstlername und Suchbegriff ergab; beide Anbieter bewerten damit
    /// denselben Teil der Hörspiel-Erkennung.
    /// </summary>
    /// <param name="ContainsQuery">Der Name enthält den Suchbegriff.</param>
    /// <param name="NumberVariantMatch">Er passt, wenn eine Ziffer gegen ihr Zahlwort getauscht wird.</param>
    /// <param name="ExactWordMatch">Jedes Wort des Suchbegriffs steht als ganzes Wort im Namen.</param>
    public readonly record struct HoerspielNameMatch(bool ContainsQuery, bool NumberVariantMatch, bool ExactWordMatch);
}
