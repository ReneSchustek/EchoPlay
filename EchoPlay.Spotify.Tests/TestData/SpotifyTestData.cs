using EchoPlay.Spotify.Dtos;

namespace EchoPlay.Spotify.Tests.TestData
{
    /// <summary>
    /// Zentrale Sammlung konsistenter Spotify-Testdaten.
    /// Die Daten bilden eine typische Hörspielstruktur ab und dienen als stabile Grundlage für Mock-Tests.
    /// </summary>
    internal static class SpotifyTestData
    {
        /// <summary>
        /// Ein einzelner Hörspiel-Künstler.
        /// Wird bewusst eindeutig gewählt, um Suchtests reproduzierbar zu halten.
        /// </summary>
        public static SpotifyArtistDto DieDreiFragezeichen => new()
        {
            SpotifyArtistId = "artist-ddf",
            Name = "Die drei ???",

            // Genres werden später für Scoring- und Filtertests relevant.
            Genres = ["hörspiel", "kinderhörspiel"],

            // Bilder sind für fachliche Tests irrelevant und werden daher bewusst weggelassen.
            ImageUrl = null
        };

        /// <summary>
        /// Erstes Album der Serie.
        /// </summary>
        public static SpotifyAlbumDto AlbumSuperPapagei => new()
        {
            SpotifyAlbumId = "album-001",
            Title = "Die drei ??? und der Super-Papagei",

            // ReleaseDate ist wichtig für Sortierungstests.
            ReleaseDate = new DateTime(1979, 1, 1),

            TotalTracks = 1
        };

        /// <summary>
        /// Ein einzelner Track, der eine komplette Hörspielfolge repräsentiert.
        /// </summary>
        public static SpotifyTrackDto Folge1 => new()
        {
            SpotifyTrackId = "track-001",
            Title = "Folge 1",
            Duration = TimeSpan.FromMinutes(45),
            TrackNumber = 1
        };

        /// <summary>
        /// Repräsentiert einen fachlich ungeeigneten Künstler.
        /// Dieser Datensatz wird bewusst generisch gehalten, da es nicht um Spotify-spezifische Details geht,
        /// sondern um die Ablehnung durch die Seriensuche.
        /// </summary>
        internal static SpotifyArtistDto UngeeigneterKuenstler => new()
        {
            SpotifyArtistId = "spotify-artist-non-hoerspiel",
            Name = "Random Pop Artist",
            Genres = ["pop"],
            ImageUrl = null
        };
    }
}