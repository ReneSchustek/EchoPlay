namespace EchoPlay.Data.Entities.Settings
{
    /// <summary>
    /// Definiert die verfügbaren Metadaten-Anbieter für Hörspielinformationen.
    /// </summary>
    public enum ProviderType
    {
        /// <summary>
        /// Holt die Daten von Spotify.
        /// </summary>
        Spotify = 0,

        /// <summary>
        /// Holt die Daten von AppleMusic.
        /// </summary>
        AppleMusic = 1,

        /// <summary>
        /// Kein Online-Dienst aktiv.
        /// Online-Mediathek und serienübergreifende Suche sind deaktiviert.
        /// </summary>
        None = 2,

        /// <summary>
        /// Beide Anbieter aktiv. Import läuft über Apple Music,
        /// Deep-Links und Cover-Suche nutzen beide Quellen.
        /// </summary>
        Both = 3
    }
}
