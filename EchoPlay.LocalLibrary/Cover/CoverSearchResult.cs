using System;
using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Ein einzelnes Ergebnis einer Online-Cover-Suche.
    /// Enthält eine kleinere Vorschau-URL für den Auswahldialog und die Originaldaten-URL
    /// für das tatsächlich zu speichernde Bild.
    /// </summary>
    public sealed class CoverSearchResult : IEquatable<CoverSearchResult>
    {
        /// <summary>
        /// Erstellt ein neues Cover-Suchergebnis.
        /// </summary>
        public CoverSearchResult(
            [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
                Justification = "Folgt dem Property-Typ (externe Such-API-Antwort als String).")]
            string ThumbnailUrl,
            [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
                Justification = "Folgt dem Property-Typ (externe Such-API-Antwort als String).")]
            string FullUrl,
            string ReleaseTitle,
            string Source)
        {
            ArgumentNullException.ThrowIfNull(ThumbnailUrl);
            ArgumentNullException.ThrowIfNull(FullUrl);
            ArgumentNullException.ThrowIfNull(ReleaseTitle);
            ArgumentNullException.ThrowIfNull(Source);
            this.ThumbnailUrl = ThumbnailUrl;
            this.FullUrl = FullUrl;
            this.ReleaseTitle = ReleaseTitle;
            this.Source = Source;
        }

        /// <summary>
        /// URL eines verkleinerten Vorschaubilds (~250 px). Wird in der Kachelansicht des Auswahldialogs verwendet.
        /// </summary>
        [SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
            Justification = "Spiegelt Such-API-Antwort (Cover Art Archive, iTunes, Discogs, Deezer); Uri-Umwandlung würde die Serialisierung und Bindung an die Kachelansicht komplizieren.")]
        public string ThumbnailUrl { get; }

        /// <summary>
        /// URL des Originalbilds in voller Auflösung. Wird heruntergeladen und gespeichert nachdem der Nutzer die Kachel bestätigt hat.
        /// </summary>
        [SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
            Justification = "Spiegelt Such-API-Antwort (Cover Art Archive, iTunes, Discogs, Deezer); Uri-Umwandlung würde die Serialisierung und Bindung an die Kachelansicht komplizieren.")]
        public string FullUrl { get; }

        /// <summary>
        /// Titel des Releases oder Albums. Erscheint als Beschriftung unterhalb der Kachel.
        /// </summary>
        public string ReleaseTitle { get; }

        /// <summary>
        /// Herkunft des Treffers, z.B. "Cover Art Archive". Dient der Transparenz gegenüber dem Nutzer.
        /// </summary>
        public string Source { get; }

        /// <inheritdoc />
        public bool Equals(CoverSearchResult? other)
        {
            if (other is null) return false;
            return ThumbnailUrl == other.ThumbnailUrl
                && FullUrl == other.FullUrl
                && ReleaseTitle == other.ReleaseTitle
                && Source == other.Source;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj) => Equals(obj as CoverSearchResult);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(ThumbnailUrl, FullUrl, ReleaseTitle, Source);
    }
}
