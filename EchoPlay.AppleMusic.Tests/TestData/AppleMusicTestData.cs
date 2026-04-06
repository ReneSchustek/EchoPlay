using EchoPlay.AppleMusic.Dtos;

namespace EchoPlay.AppleMusic.Tests.TestData
{
    /// <summary>
    /// Zentrale Sammlung konsistenter iTunes-Testdaten.
    /// Die Daten bilden eine typische Hörspielstruktur ab und dienen als stabile Grundlage für Fake-Tests.
    /// </summary>
    internal static class AppleMusicTestData
    {
        /// <summary>
        /// Ein typischer Hörspiel-Künstler mit Hörspiel-Genre.
        /// Wird bewusst eindeutig gewählt, um Suchtests reproduzierbar zu halten.
        /// </summary>
        public static ITunesArtistDto DieDreiFragezeichen => new()
        {
            WrapperType = "artist",
            ArtistId = 201306317,
            ArtistName = "Die drei ???",
            PrimaryGenreName = "Hörspiele",
            ArtistLinkUrl = "https://music.apple.com/de/artist/die-drei/201306317"
        };

        /// <summary>
        /// Repräsentiert einen fachlich ungeeigneten Künstler.
        /// Dieser Datensatz wird bewusst generisch gehalten, da es nicht um iTunes-spezifische Details geht,
        /// sondern um die Ablehnung durch die Seriensuche.
        /// </summary>
        public static ITunesArtistDto UngeeigneterKuenstler => new()
        {
            WrapperType = "artist",
            ArtistId = 999999999,
            ArtistName = "Random Pop Artist",
            PrimaryGenreName = "Pop",
            ArtistLinkUrl = null
        };

        /// <summary>
        /// Ein Hörspiel-Album mit typischer Struktur.
        /// </summary>
        public static ITunesCollectionDto AlbumSuperPapagei => new()
        {
            WrapperType = "collection",
            CollectionId = 1683337919,
            CollectionName = "Folge 1: und der Super-Papagei",
            ArtistId = 201306317,
            ArtistName = "Die drei ???",
            TrackCount = 5,
            ArtworkUrl100 = "https://example.com/artwork100.jpg",
            ReleaseDate = "1979-01-01T07:00:00Z",
            CollectionViewUrl = "https://music.apple.com/de/album/1683337919",
            PrimaryGenreName = "Hörspiele"
        };

        /// <summary>
        /// Ein einzelner Hörspiel-Track (45 Minuten).
        /// </summary>
        public static ITunesTrackDto Folge1Track => new()
        {
            WrapperType = "track",
            TrackId = 100001,
            TrackName = "Folge 1 - Der Super-Papagei",
            TrackTimeMillis = (int)TimeSpan.FromMinutes(45).TotalMilliseconds,
            TrackNumber = 1,
            ReleaseDate = "1979-01-01T07:00:00Z",
            CollectionId = 1683337919,
            CollectionName = "Folge 1: und der Super-Papagei"
        };
    }
}
