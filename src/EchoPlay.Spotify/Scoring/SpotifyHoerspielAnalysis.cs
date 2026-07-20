using EchoPlay.Core.Scoring;

namespace EchoPlay.Spotify.Scoring
{
    /// <summary>
    /// Internes Analyse-Ergebnis der Spotify-Hörspiel-Heuristik.
    /// Enthält ausschließlich Boolean-Flags und Debug-Informationen,
    /// die vom Scorer zur Score-Berechnung herangezogen werden.
    /// </summary>
    internal sealed class SpotifyHoerspielAnalysis : IHoerspielAnalysis
    {
        /// <summary>
        /// Gibt an, ob mindestens ein Genre des Künstlers ein negatives Musik-Genre ist.
        /// </summary>
        public bool HasNegativeMusicGenre { get; init; }

        /// <summary>
        /// Gibt an, ob der Künstlername einer bekannten Hörspielserie entspricht.
        /// </summary>
        public bool IsKnownSeries { get; init; }

        /// <summary>
        /// Gibt an, ob der Suchbegriff im Künstlernamen enthalten ist (Contains-Match).
        /// </summary>
        public bool NameContainsQuery { get; init; }

        /// <summary>
        /// Gibt an, ob eine Zahlwort-Variante des Suchbegriffs im Künstlernamen enthalten ist.
        /// </summary>
        public bool HasNumberVariantMatch { get; init; }

        /// <summary>
        /// Gibt an, ob der Suchbegriff als eigenständiges Wort im Künstlernamen vorkommt.
        /// </summary>
        public bool HasExactWordMatch { get; init; }

        /// <summary>
        /// Gibt an, ob mindestens ein Album die typische Hörspiel-Struktur aufweist.
        /// </summary>
        public bool HasHoerspielAlbumStructure { get; init; }

        /// <summary>
        /// Gibt an, ob der Künstler überhaupt Alben besitzt.
        /// </summary>
        public bool HasAlbums { get; init; }

        /// <summary>
        /// Menschenlesbare Debug-Information zur Analyse.
        /// </summary>
        public string DebugInfo { get; init; } = string.Empty;
    }
}
