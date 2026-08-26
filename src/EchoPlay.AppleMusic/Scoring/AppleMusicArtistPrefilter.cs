using EchoPlay.AppleMusic.Dtos;
using EchoPlay.Core.Scoring;

namespace EchoPlay.AppleMusic.Scoring
{
    /// <summary>
    /// Ein Künstler, der die Vorauswahl bestanden hat, samt der Merkmale, die ihn hineingebracht haben.
    /// </summary>
    /// <param name="Artist">Der Künstler aus der Anbieter-Antwort.</param>
    /// <param name="IsKnownSeries">
    /// Ob der Name einer bekannten Hörspielserie entspricht. Solche Kandidaten sind ohne
    /// eine einzige weitere Anfrage entschieden.
    /// </param>
    /// <param name="Relevance">
    /// Rangwert für die Reihenfolge der Bewertung — je höher, desto eher wird der Künstler
    /// geprüft. Sorgt dafür, dass die aussichtsreichen Treffer zuerst auf dem Schirm stehen.
    /// </param>
    internal readonly record struct AppleMusicArtistCandidate(
        ITunesArtistDto Artist,
        bool IsKnownSeries,
        int Relevance);

    /// <summary>
    /// Das Ergebnis der Vorauswahl.
    /// </summary>
    /// <param name="Candidates">Die zu bewertenden Künstler, aussichtsreichste zuerst.</param>
    /// <param name="Rejected">Wie viele Künstler ohne Namensbezug und ohne Hörspiel-Genre ausschieden.</param>
    internal sealed record AppleMusicPrefilterResult(
        IReadOnlyList<AppleMusicArtistCandidate> Candidates,
        int Rejected);

    /// <summary>
    /// Wählt aus der Künstler-Antwort die Kandidaten aus, für die sich die teure Albenprüfung lohnt.
    /// </summary>
    /// <remarks>
    /// Die Künstlersuche liefert bis zu 25 Namen, von denen die meisten nichts mit dem
    /// Suchbegriff zu tun haben — bei „Bibi" etwa Musikkünstler aus aller Welt. Jeder von
    /// ihnen kostete bisher mehrere Anfragen an der Ratenbremse. Genre und Name stehen
    /// bereits in der Antwort; wer nach beiden Merkmalen nichts hergibt, kann auch durch
    /// eine Albenprüfung nicht mehr zum Hörspiel werden.
    /// </remarks>
    internal static class AppleMusicArtistPrefilter
    {
        // Rangwerte der Vorauswahl. Die Abstände sind bewusst grob: Sie ordnen die
        // Kandidaten, sie berechnen keine Bewertung — die macht der Scorer.
        private const int KnownSeriesRelevance = 1000;
        private const int ExactWordMatchRelevance = 100;
        private const int NameContainsRelevance = 50;
        private const int GenreRelevance = 20;

        /// <summary>
        /// Trifft die Vorauswahl und bringt sie in die Reihenfolge der Bewertung.
        /// </summary>
        /// <param name="artists">Die Künstler aus der Anbieter-Antwort.</param>
        /// <param name="searchQuery">Der ursprüngliche Suchbegriff.</param>
        /// <param name="settings">Die Bewertungsregeln mit bekannten Serien und Genres.</param>
        /// <returns>Die Kandidaten und die Zahl der ausgeschiedenen Künstler.</returns>
        public static AppleMusicPrefilterResult SelectCandidates(
            IReadOnlyList<ITunesArtistDto> artists,
            string searchQuery,
            AppleMusicHoerspielSettings settings)
        {
            ArgumentNullException.ThrowIfNull(artists);
            ArgumentNullException.ThrowIfNull(settings);

            string normalizedQuery = HoerspielTextNormalizer.Normalize(searchQuery);
            List<AppleMusicArtistCandidate> candidates = new(artists.Count);
            int rejected = 0;

            foreach (ITunesArtistDto artist in artists)
            {
                AppleMusicArtistCandidate? candidate = Evaluate(artist, normalizedQuery, settings);

                if (candidate is null)
                {
                    rejected++;
                    continue;
                }

                candidates.Add(candidate.Value);
            }

            // Absteigend nach Rang, bei Gleichstand in der Reihenfolge des Anbieters:
            // Der stellt seine Treffer bereits nach eigener Relevanz zusammen.
            List<AppleMusicArtistCandidate> ordered = candidates
                .OrderByDescending(candidate => candidate.Relevance)
                .ToList();

            return new AppleMusicPrefilterResult(ordered, rejected);
        }

        /// <summary>
        /// Bewertet einen einzelnen Künstler für die Vorauswahl.
        /// </summary>
        /// <param name="artist">Der Künstler.</param>
        /// <param name="normalizedQuery">Der bereits normalisierte Suchbegriff.</param>
        /// <param name="settings">Die Bewertungsregeln.</param>
        /// <returns>Der Kandidat, oder <see langword="null"/>, wenn er ausscheidet.</returns>
        private static AppleMusicArtistCandidate? Evaluate(
            ITunesArtistDto artist,
            string normalizedQuery,
            AppleMusicHoerspielSettings settings)
        {
            if (HoerspielNameMatcher.IsKnownSeries(artist.ArtistName, settings.DefaultKnownSeries))
            {
                return new AppleMusicArtistCandidate(artist, IsKnownSeries: true, KnownSeriesRelevance);
            }

            string normalizedName = HoerspielTextNormalizer.Normalize(artist.ArtistName);
            int relevance = 0;

            if (HoerspielNameMatcher.IsExactWordMatch(normalizedName, normalizedQuery))
            {
                relevance += ExactWordMatchRelevance;
            }

            if (normalizedName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || HoerspielNameMatcher.HasNumberVariantMatch(normalizedName, normalizedQuery, settings.NumberWordMapping))
            {
                relevance += NameContainsRelevance;
            }

            if (AppleMusicGenreMatcher.IsHoerspielGenre(artist.PrimaryGenreName, settings.HoerspielGenres))
            {
                relevance += GenreRelevance;
            }

            return relevance == 0
                ? null
                : new AppleMusicArtistCandidate(artist, IsKnownSeries: false, relevance);
        }
    }
}
