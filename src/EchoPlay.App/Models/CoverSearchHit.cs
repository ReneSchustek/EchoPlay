namespace EchoPlay.App.Models
{
    /// <summary>
    /// App-internes Display-Modell für ein Online-Cover-Suchergebnis.
    /// Kapselt das LocalLibrary-Suchergebnis, damit UI-Helfer und View-Models
    /// nicht direkt vom <see cref="EchoPlay.LocalLibrary.Cover.CoverSearchResult"/>
    /// abhängen müssen. Mapping erfolgt im <see cref="CoverSearchHit.From"/>-Helper.
    /// </summary>
    /// <param name="ThumbnailUrl">Adresse des Vorschaubilds für die Kachelansicht, meist 250 Pixel breit.</param>
    /// <param name="FullUrl">Adresse des Originalbilds in voller Auflösung, die heruntergeladen wird.</param>
    /// <param name="ReleaseTitle">Titel des Releases/Albums für die Kachelbeschriftung.</param>
    /// <param name="Source">Herkunft des Treffers, etwa "Cover Art Archive".</param>
    public sealed record CoverSearchHit(
        Uri ThumbnailUrl,
        Uri FullUrl,
        string ReleaseTitle,
        string Source)
    {
        /// <summary>
        /// Erstellt einen App-Wrapper aus einem LocalLibrary-Suchergebnis.
        /// Wird von den Mediathek-ViewModels genutzt, bevor Treffer an die UI weitergereicht werden.
        /// </summary>
        /// <param name="result">Das LocalLibrary-Suchergebnis.</param>
        public static CoverSearchHit From(EchoPlay.LocalLibrary.Cover.CoverSearchResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            return new(result.ThumbnailUrl, result.FullUrl, result.ReleaseTitle, result.Source);
        }
    }
}
