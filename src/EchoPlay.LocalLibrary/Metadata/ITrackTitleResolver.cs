namespace EchoPlay.LocalLibrary.Metadata
{
    /// <summary>
    /// Ermittelt die Anzeigenamen einer Trackliste aus den Kennzeichnungen der Dateien.
    /// Entkoppelt die ViewModels vom Dateizugriff und macht die Rückfallebene testbar.
    /// </summary>
    public interface ITrackTitleResolver
    {
        /// <summary>
        /// Liest die Titel der übergebenen Spuren und liefert für jede einen Anzeigenamen.
        /// Fehlt der Titel oder ist die Datei nicht lesbar, steht dort der aufgeräumte Dateiname.
        /// </summary>
        /// <param name="tracks">Die Spuren in Anzeigereihenfolge.</param>
        /// <param name="cancellationToken">Bricht das Einlesen ab.</param>
        /// <returns>
        /// Ein Anzeigename je Spur, in derselben Reihenfolge und Anzahl wie <paramref name="tracks"/>.
        /// Kein Eintrag ist leer.
        /// </returns>
        Task<IReadOnlyList<string>> ResolveAsync(
            IReadOnlyList<TrackTitleRequest> tracks,
            CancellationToken cancellationToken = default);
    }
}
