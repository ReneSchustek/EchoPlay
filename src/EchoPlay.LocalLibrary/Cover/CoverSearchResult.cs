using System;

namespace EchoPlay.LocalLibrary.Cover
{
    /// <summary>
    /// Ein einzelnes Ergebnis einer Online-Cover-Suche.
    /// Enthält eine kleinere Vorschau-Adresse für den Auswahldialog und die Adresse des
    /// Originalbilds, das tatsächlich gespeichert wird. Beide sind beim Erzeugen bereits als
    /// Web-Adresse geprüft.
    /// </summary>
    public sealed class CoverSearchResult : IEquatable<CoverSearchResult>
    {
        /// <summary>
        /// Erstellt ein neues Cover-Suchergebnis.
        /// </summary>
        public CoverSearchResult(
            Uri ThumbnailUrl,
            Uri FullUrl,
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
        /// URL eines verkleinerten Vorschaubilds, meist 250 Pixel breit, für die Kacheln des Auswahldialogs.
        /// </summary>
        public Uri ThumbnailUrl { get; }

        /// <summary>
        /// URL des Originalbilds in voller Auflösung. Heruntergeladen wird es erst, wenn der Nutzer die Kachel bestätigt.
        /// </summary>
        public Uri FullUrl { get; }

        /// <summary>
        /// Titel des Releases oder Albums. Erscheint als Beschriftung unterhalb der Kachel.
        /// </summary>
        public string ReleaseTitle { get; }

        /// <summary>
        /// Herkunft des Treffers, etwa "Cover Art Archive".
        /// </summary>
        public string Source { get; }

        /// <inheritdoc />
        public bool Equals(CoverSearchResult? other)
        {
            if (other is null)
            {
                return false;
            }

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
