namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Die vier Teilbereiche der lokalen Mediathek. Die Aktionen bekommen sie gebündelt, weil sie
    /// immer gemeinsam auftreten und unverändert vom ViewModel der Seite kommen.
    /// </summary>
    /// <param name="Scan">Bereich für Einlesen und Ordnerauswahl.</param>
    /// <param name="Artists">Bereich der Serienkacheln.</param>
    /// <param name="Episodes">Bereich der Folgenkacheln.</param>
    /// <param name="Tracks">Bereich der Spuren.</param>
    internal sealed record LocalLibraryParts(
        LocalLibraryScanViewModel Scan,
        LocalArtistsViewModel Artists,
        LocalEpisodesViewModel Episodes,
        LocalTracksViewModel Tracks);
}
