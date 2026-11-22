using EchoPlay.App.Models;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Was eine Folgenkarte der Startseite anzeigt. Die Karte bekommt dazu ihr Cover und ihre
    /// Dienste; so bleibt ihr Konstruktor kurz und jeder Wert benannt.
    /// </summary>
    public sealed record NewEpisodeCardData
    {
        /// <summary>ID der darzustellenden Episode.</summary>
        public required Guid EpisodeId { get; init; }

        /// <summary>ID der zugehörigen Serie.</summary>
        public required Guid SeriesId { get; init; }

        /// <summary>Anzeigename der Serie.</summary>
        public required string SeriesName { get; init; }

        /// <summary>Titel der Episode.</summary>
        public required string EpisodeTitle { get; init; }

        /// <summary>Aktueller Wiedergabestatus.</summary>
        public PlaybackStatus Status { get; init; }

        /// <summary>Fortschritt in Prozent (0–100) für den Fortschrittsbalken.</summary>
        public double ProgressPercent { get; init; }

        /// <summary>Ob lokale Audiodateien vorhanden sind.</summary>
        public bool HasLocalTrack { get; init; }

        /// <summary>Ob die Episode nur angekündigt, also noch nicht verfügbar ist.</summary>
        public bool IsAnnounced { get; init; }

        /// <summary>Folgennummer für die Anzeige, oder <see langword="null"/>, wenn nicht bekannt.</summary>
        public int? EpisodeNumber { get; init; }

        /// <summary>Erscheinungsdatum für Ankündigungen, oder <see langword="null"/>.</summary>
        public DateTime? ReleaseDate { get; init; }

        /// <summary>Spotify-Album-Kennung der Folge, sofern bekannt.</summary>
        public string? SpotifyAlbumId { get; init; }

        /// <summary>Apple-Music-Album-Kennung der Folge, sofern bekannt.</summary>
        public string? AppleMusicAlbumId { get; init; }
    }
}
