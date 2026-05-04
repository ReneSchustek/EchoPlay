namespace EchoPlay.App.Services
{
    /// <summary>
    /// Logische Ziel-Seiten der Anwendung.
    /// ViewModels nennen nur diese Enum-Werte und kennen keine Page-Typen –
    /// so bleiben sie frei von WinUI-Abhängigkeiten und testbar.
    /// </summary>
    public enum NavigationTarget
    {
        /// <summary>Startseite mit Neuerscheinungen, Favoriten, Weiterhören.</summary>
        Dashboard,

        /// <summary>Online-Mediathek (Provider-Inhalte).</summary>
        MediathekOnline,

        /// <summary>Lokale Mediathek (gescannte Audiodateien).</summary>
        MediathekLokal,

        /// <summary>Online-Suche nach Serien.</summary>
        Suche,

        /// <summary>Vollständiger Player mit Playlist-Verwaltung.</summary>
        Player,

        /// <summary>Einstellungen.</summary>
        Settings,

        /// <summary>Tag-Manager für Audiodatei-Metadaten.</summary>
        TagManager,

        /// <summary>Detailansicht einer Serie. Parameter: <see cref="System.Guid"/> der SeriesId.</summary>
        SeriesDetail,

        /// <summary>Import-Seite nach Provider-Auswahl.</summary>
        Import,

        /// <summary>Statistik-Seite.</summary>
        Statistik,

        /// <summary>Protokoll/Log-Anzeige.</summary>
        Protokoll,

        /// <summary>Über-Seite (Version, Autoren, Lizenz).</summary>
        Über
    }
}
