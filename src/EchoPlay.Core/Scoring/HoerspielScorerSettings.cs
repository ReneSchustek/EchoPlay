using System.Collections.ObjectModel;

namespace EchoPlay.Core.Scoring
{
    /// <summary>
    /// Gemeinsame Basis-Einstellungen für die Hörspiel-Bewertung.
    /// Enthält alle Parameter, die bei Spotify und Apple Music identisch sind.
    /// Provider-spezifische Settings erben von dieser Klasse und ergänzen eigene Werte.
    /// </summary>
    public abstract class HoerspielScorerSettings
    {
        /// <summary>
        /// Namen bekannter Hörspielserien, die bei Übereinstimmung zur sofortigen Akzeptanz führen.
        /// </summary>
        public Collection<string> DefaultKnownSeries { get; init; } =
        [
            "Die drei ???",
            "Die drei ??? Kids",
            "TKKG",
            "Fünf Freunde",
            "Benjamin Blümchen",
            "Bibi Blocksberg",
            "Bibi und Tina",
            "Pumuckl",
            "Die drei Ausrufezeichen",
            "Jan Tenner",
            "John Sinclair",
            "Larry Brent",
            "Geisterjäger John Sinclair",
            "Europa Originale"
        ];

        /// <summary>
        /// Mindestscore, ab dem ein Künstler als Hörspiel akzeptiert wird.
        /// </summary>
        public int MinimumScoreThreshold { get; init; } = 50;

        /// <summary>
        /// Bonus, wenn der Suchbegriff im Künstlernamen enthalten ist (Contains-Match).
        /// </summary>
        public int NameContainsBonus { get; init; } = 50;

        /// <summary>
        /// Bonus für exaktes Wort-Matching (Suchbegriff als eigenständiges Wort im Namen).
        /// </summary>
        public int ExactWordMatchBonus { get; init; } = 25;

        /// <summary>
        /// Bonus, wenn Album-Strukturen typische Hörspiel-Merkmale aufweisen.
        /// </summary>
        public int AlbumStructureBonus { get; init; } = 25;

        /// <summary>
        /// Abzug, wenn keine Alben mit Hörspiel-Struktur gefunden werden.
        /// </summary>
        public int NoAlbumPenalty { get; init; } = -25;

        /// <summary>
        /// Anzahl der Alben, die für die Struktur-Analyse geprüft werden.
        /// </summary>
        public int AlbumsToCheck { get; init; } = 3;

        /// <summary>
        /// Zuordnung von Ziffern zu Zahlwörtern für Namensvarianten-Erkennung.
        /// Ermöglicht z.B. den Abgleich von "Die 3 ???" mit "Die drei ???".
        /// </summary>
        public Dictionary<string, string> NumberWordMapping { get; init; } = new()
        {
            ["3"] = "drei",
            ["5"] = "fünf",
            ["7"] = "sieben"
        };
    }
}
