namespace EchoPlay.App.Services
{
    /// <summary>
    /// String-Schlüssel der Keyed-DI-Registrierungen für Provider-Services
    /// (<c>ISeriesImportSearch</c>, <c>IEpisodeImportSource</c>) und gleichzeitig
    /// <c>Source</c>-Wert in <c>ImportSeries</c>/<c>ImportEpisode</c>. Korrespondiert
    /// mit <c>ProviderType.ToString()</c>; getrennte Konstante, weil das Enum in
    /// <c>EchoPlay.Data.Entities.Settings</c> liegt und die App-Schicht hier keine
    /// Daten-Abhängigkeit aufnehmen will.
    /// </summary>
    internal static class ProviderKeys
    {
        public const string Spotify = "Spotify";
        public const string AppleMusic = "AppleMusic";

        /// <summary>Beide Anbieter zugleich; nur als Auswahl in den Einstellungen.</summary>
        public const string Both = "Both";

        /// <summary>
        /// Quelle eines Treffers aus dem eigenen Bestand. Kein Anbieter-Schlüssel; die
        /// Cover-Pipeline erkennt daran, dass die Kennung eine Datenbank-Kennung ist. Die
        /// Suchergebnisse zeigen dafür die übersetzte Beschriftung, nicht diesen Wert.
        /// </summary>
        public const string Local = "Local";
    }
}
