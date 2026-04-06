namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Ein einzelnes Ergebnis einer Online-Cover-Suche.
    /// Enthält eine kleinere Vorschau-URL für den Auswahldialog und die Originaldaten-URL
    /// für das tatsächlich zu speichernde Bild.
    /// </summary>
    /// <param name="ThumbnailUrl">
    /// URL eines verkleinerten Vorschaubilds (~250 px). Wird in der Kachelansicht des Auswahldialogs verwendet.
    /// </param>
    /// <param name="FullUrl">
    /// URL des Originalbilds in voller Auflösung. Wird heruntergeladen und gespeichert nachdem der Nutzer die Kachel bestätigt hat.
    /// </param>
    /// <param name="ReleaseTitle">
    /// Titel des Releases oder Albums. Erscheint als Beschriftung unterhalb der Kachel.
    /// </param>
    /// <param name="Source">
    /// Herkunft des Treffers, z.B. "Cover Art Archive". Dient der Transparenz gegenüber dem Nutzer.
    /// </param>
    public sealed record CoverSearchResult(
        string ThumbnailUrl,
        string FullUrl,
        string ReleaseTitle,
        string Source);
}
