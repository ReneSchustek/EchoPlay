namespace EchoPlay.Core.Models.Import
{
    /// <summary>
    /// Repräsentiert eine potenzielle Hörspielserie, die aus einer externen Quelle importiert werden kann.
    /// Die Instanz ist noch nicht Teil der lokalen Bibliothek und besitzt keinen Persistenz- oder Lebenszykluszustand.
    /// </summary>
    public sealed class ImportSeries
    {
        /// <summary>
        /// Eindeutige Kennung der Serie innerhalb der jeweiligen Importquelle.
        /// Die Bedeutung des Wertes ist anbieterabhängig (z.B. Spotify-Artist-ID, Apple-Music-Artist-ID),
        /// wird jedoch im Core bewusst neutral behandelt.
        /// </summary>
        public required string SourceSeriesId { get; init; }

        /// <summary>
        /// Bezeichner der Importquelle.
        /// Beispiel: "Spotify", "AppleMusic".
        /// </summary>
        public required string Source { get; init; }

        /// <summary>
        /// Titel der Hörspielserie.
        /// </summary>
        public required string Title { get; init; }

        /// <summary>
        /// Optionale Beschreibung der Serie, sofern vom Anbieter geliefert.
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// URL zum Coverbild der Serie.
        /// </summary>
        public string? CoverImageUrl { get; init; }

        /// <summary>
        /// Gibt an, ob die Serie anhand fachlicher Heuristiken als Hörspiel erkannt wurde.
        /// </summary>
        public bool IsHoerspiel { get; init; }

        /// <summary>
        /// Fachlicher Score zur Einschätzung der Hörspiel-Relevanz. Höhere Werte bedeuten eine höhere Sicherheit.
        /// </summary>
        public int Score { get; init; }

        /// <summary>
        /// Gibt an, ob dieses Suchergebnis ein Album (= einzelne Folge) statt einer Serie ist.
        /// Bei Album-Ergebnissen ist <see cref="Title"/> der Folgenname und
        /// <see cref="ArtistName"/> der Serienname.
        /// </summary>
        public bool IsAlbumResult { get; init; }

        /// <summary>
        /// Name des Künstlers/der Serie. Nur bei Album-Suchergebnissen befüllt.
        /// Zeigt dem Nutzer, zu welcher Serie die gefundene Folge gehört.
        /// </summary>
        public string? ArtistName { get; init; }
    }
}
