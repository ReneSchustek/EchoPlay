using EchoPlay.Data.Entities.Common;
using System.Diagnostics.CodeAnalysis;

namespace EchoPlay.Data.Entities.Library
{
    /// <summary>
    /// Repräsentiert eine Hörspielserie
    /// (z. B. "Die drei ???", "Fünf Freunde").
    /// </summary>
    public class Series : BaseEntity
    {
        /// <summary>
        /// Titel der Hörspielserie.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Beschreibung oder Zusatzinformationen zur Serie.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// URL oder lokaler Pfad zum Coverbild der Serie.
        /// </summary>
        [SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
            Justification = "Entity spiegelt DB-Spalte CoverImageUrl (URL oder lokaler Pfad als TEXT); Uri-Umwandlung ist für lokale Pfade nicht passend und würde EF-Core-Mapping erfordern.")]
        public string? CoverImageUrl { get; set; }

        /// <summary>
        /// Artist-ID der Serie bei Spotify.
        /// Wird gesetzt, wenn die Serie von Spotify importiert wurde.
        /// </summary>
        public string? SpotifyArtistId { get; set; }

        /// <summary>
        /// Artist-ID der Serie bei Apple Music.
        /// Wird gesetzt, wenn die Serie von Apple Music importiert wurde.
        /// </summary>
        public string? AppleMusicArtistId { get; set; }

        /// <summary>
        /// Gibt an, ob die Serie aktiv fortgeführt wird
        /// oder offiziell abgeschlossen ist.
        /// </summary>
        public bool IsCompleted { get; set; }

        /// <summary>
        /// Pfad zum lokalen Serienordner auf dem Dateisystem.
        /// Nur gesetzt, wenn die lokale Bibliothek aktiviert und ein Ordner zugeordnet wurde.
        /// </summary>
        public string? LocalFolderPath { get; set; }

        /// <summary>
        /// Gibt an, ob die Serie über eine Online-Suche (Spotify/Apple Music) importiert wurde.
        /// Nur Serien mit diesem Flag erscheinen in der Online-Mediathek.
        /// Wird ausschließlich vom ImportService beim expliziten Import gesetzt –
        /// nicht vom OnlineEpisodeChecker bei der automatischen iTunes-Prüfung.
        /// Standard <see langword="false"/> – lokal gescannte Serien sind nicht online-importiert.
        /// </summary>
        public bool IsOnlineImported { get; set; }

        /// <summary>
        /// Gibt an, ob der Nutzer diese Serie abonniert hat.
        /// Nur abonnierte Serien erscheinen im Dashboard und in der Mediathek.
        /// Standard <see langword="false"/> – bestehende Serien gelten nach der Migration als nicht abonniert.
        /// </summary>
        public bool IsSubscribed { get; set; }

        /// <summary>
        /// Gibt an, ob der Nutzer diese Serie als Favorit markiert hat.
        /// Nur Folgen favorisierter Serien erscheinen im Dashboard-Abschnitt „Neuerscheinungen".
        /// <see cref="IsSubscribed"/> und <see cref="IsFavorite"/> sind unabhängig voneinander:
        /// Man kann eine Serie abonnieren (in der Bibliothek haben), ohne sie zu favorisieren,
        /// und umgekehrt.
        /// Standard <see langword="false"/> – bestehende Serien sind nach der Migration nicht favorisiert.
        /// </summary>
        public bool IsFavorite { get; set; }

        /// <summary>
        /// Gibt an, ob die Serie auf Neuerscheinungen und Ankündigungen überwacht wird.
        /// Nur überwachte Serien erscheinen im Dashboard unter Neuerscheinungen.
        /// Standard <see langword="false"/> – der Nutzer entscheidet bewusst, welche Serien überwacht werden.
        /// </summary>
        public bool IsWatched { get; set; }

        /// <summary>
        /// Erkanntes oder manuell gesetztes Ordnernamens-Muster dieser Serie.
        /// Wenn gesetzt, wird dieses Muster beim Scan dieser Serie gegenüber dem globalen
        /// Muster aus den Einstellungen bevorzugt.
        /// Beispiel: <c>"{number:000} - {title}"</c>.
        /// <see langword="null"/> bedeutet, dass das globale Muster verwendet wird.
        /// </summary>
        public string? FolderPattern { get; set; }
    }
}
