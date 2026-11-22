using EchoPlay.App.Models;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Was eine Folgenkachel anzeigt. Die Kachel selbst bekommt dazu nur noch ihren Befehl, das
    /// Cover und die Lokalisierung; so bleibt ihr Konstruktor kurz und jeder Wert benannt.
    /// </summary>
    public sealed record EpisodeTileData
    {
        /// <summary>Datenbank-ID der Episode.</summary>
        public required Guid EpisodeId { get; init; }

        /// <summary>Episodennummer, oder <see langword="null"/>, wenn keine vorliegt.</summary>
        public int? EpisodeNumber { get; init; }

        /// <summary>Episodentitel.</summary>
        public required string Title { get; init; }

        /// <summary>Gesamtdauer, oder <see langword="null"/>, wenn unbekannt.</summary>
        public TimeSpan? TotalDuration { get; init; }

        /// <summary>Wiedergabestatus der Episode.</summary>
        public PlaybackStatus PlaybackStatus { get; init; }

        /// <summary>Erscheinungsdatum, oder <see langword="null"/>.</summary>
        public DateTime? ReleaseDate { get; init; }

        /// <summary>Wiedergabefortschritt in Prozent (0–100).</summary>
        public double ProgressPercent { get; init; }

        /// <summary>Ob es sich um eine Sonderfolge handelt.</summary>
        public bool IsSpecialEpisode { get; init; }

        /// <summary>Spotify-Album-ID der Folge, sofern bekannt.</summary>
        public string? SpotifyAlbumId { get; init; }

        /// <summary>Ob mindestens eine lokale Audiodatei vorliegt.</summary>
        public bool HasLocalTrack { get; init; } = true;

        /// <summary>Apple-Music-Album-ID der Folge, sofern bekannt.</summary>
        public string? AppleMusicAlbumId { get; init; }

        /// <summary>Serientitel als Suchbegriff für Spotify, wenn keine Album-ID vorliegt.</summary>
        public string? SeriesTitle { get; init; }

        /// <summary>Ob die Folge an einer offenen Stelle steht, an der weitergehört werden kann.</summary>
        public bool HasOpenPosition { get; init; }
    }
}
