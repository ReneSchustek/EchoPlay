namespace EchoPlay.Core.Scoring
{
    /// <summary>
    /// Provider-neutrale Analyse-Flags, die die gemeinsame Score-Arithmetik benötigt.
    /// Die provider-spezifischen Analyse-Ergebnisse (Spotify/Apple Music) implementieren
    /// diese Schnittstelle und ergänzen eigene Felder.
    /// </summary>
    public interface IHoerspielAnalysis
    {
        /// <summary>Ob der Künstlername einer bekannten Hörspielserie entspricht (harte Akzeptanz).</summary>
        bool IsKnownSeries { get; }

        /// <summary>Ob der Suchbegriff im Künstlernamen enthalten ist.</summary>
        bool NameContainsQuery { get; }

        /// <summary>Ob eine Zahlwort-Variante des Suchbegriffs im Namen vorkommt.</summary>
        bool HasNumberVariantMatch { get; }

        /// <summary>Ob der Suchbegriff als eigenständiges Wort im Namen vorkommt.</summary>
        bool HasExactWordMatch { get; }

        /// <summary>Ob die Album-Struktur typische Hörspiel-Merkmale zeigt.</summary>
        bool HasHoerspielAlbumStructure { get; }

        /// <summary>Menschenlesbare Zusammenfassung der Analyse-Indikatoren.</summary>
        string DebugInfo { get; }
    }
}
