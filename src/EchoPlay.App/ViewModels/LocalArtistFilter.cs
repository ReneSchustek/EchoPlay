using System;

namespace EchoPlay.App.ViewModels
{
    /// <summary>
    /// Die Kriterien, nach denen die lokale Mediathek ihre Serien einschränkt: ein Suchtext
    /// und drei Schalter.
    /// <para>
    /// Als eigener Typ, damit das Serien-ViewModel die Frage „passt diese Kachel" nicht
    /// zusätzlich zum Laden, Filtern der Auswahl und Cover-Aufbau mitträgt.
    /// </para>
    /// </summary>
    public sealed class LocalArtistFilter
    {
        /// <summary>Freitext über den Serientitel. Leer heißt: keine Einschränkung.</summary>
        public string SearchText { get; set; } = string.Empty;

        /// <summary>Nur Serien, die als Favorit markiert sind.</summary>
        public bool FavoritesOnly { get; set; }

        /// <summary>Nur Serien, die auf neue Folgen geprüft werden.</summary>
        public bool WatchedOnly { get; set; }

        /// <summary>Nur Serien, denen lokal Folgen fehlen.</summary>
        public bool IncompleteOnly { get; set; }

        /// <summary>Ob überhaupt eingeschränkt wird.</summary>
        public bool IsActive =>
            !string.IsNullOrWhiteSpace(SearchText) || FavoritesOnly || WatchedOnly || IncompleteOnly;

        /// <summary>Setzt alle Kriterien zurück.</summary>
        public void Reset()
        {
            SearchText = string.Empty;
            FavoritesOnly = false;
            WatchedOnly = false;
            IncompleteOnly = false;
        }

        /// <summary>
        /// Prüft eine Kachel gegen Suchtext und alle aktiven Schalter.
        /// <para>
        /// Mehrere Schalter wirken zusammen, nicht alternativ — wer „Favoriten" und
        /// „Unvollständig" wählt, sucht die Schnittmenge.
        /// </para>
        /// </summary>
        /// <param name="card">Die zu prüfende Serienkachel.</param>
        /// <returns><see langword="true"/>, wenn die Kachel sichtbar bleibt.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="card"/> ist <see langword="null"/>.</exception>
        public bool Matches(LocalArtistCardViewModel card)
        {
            ArgumentNullException.ThrowIfNull(card);

            if (!string.IsNullOrWhiteSpace(SearchText)
                && !card.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (FavoritesOnly && !card.IsFavorite)
            {
                return false;
            }

            if (WatchedOnly && !card.IsWatched)
            {
                return false;
            }

            return !IncompleteOnly || card.IsIncomplete;
        }
    }
}
