using System;

namespace EchoPlay.App.Models
{
    /// <summary>
    /// App-internes Display-Modell für ein Online-Cover-Suchergebnis.
    /// Kapselt das LocalLibrary-Suchergebnis, damit UI-Helfer und View-Models
    /// nicht direkt vom <see cref="EchoPlay.LocalLibrary.Cover.CoverSearchResult"/>
    /// abhängen müssen. Mapping erfolgt im <see cref="CoverSearchHit.From"/>-Helper.
    /// </summary>
    /// <param name="ThumbnailUrl">URL des Vorschaubilds (~250 px) für die Kachelansicht.</param>
    /// <param name="FullUrl">URL des Originalbilds in voller Auflösung – wird heruntergeladen.</param>
    /// <param name="ReleaseTitle">Titel des Releases/Albums – Kachelbeschriftung.</param>
    /// <param name="Source">Herkunft des Treffers (z.B. "Cover Art Archive") – Transparenz für den Nutzer.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "DTO hält URL-Rohstrings aus externen Cover-Suchen (Cover Art Archive, iTunes), die ohne Validierung an Image-Kontrollen weitergereicht werden.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "DTO hält URL-Rohstrings aus externen Cover-Suchen (Cover Art Archive, iTunes), die ohne Validierung an Image-Kontrollen weitergereicht werden.")]
    public sealed record CoverSearchHit(
        string ThumbnailUrl,
        string FullUrl,
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
