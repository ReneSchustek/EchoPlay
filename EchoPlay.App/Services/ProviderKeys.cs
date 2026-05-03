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
    }
}
