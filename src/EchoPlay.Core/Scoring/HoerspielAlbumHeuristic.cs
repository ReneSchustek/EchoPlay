namespace EchoPlay.Core.Scoring
{
    /// <summary>
    /// Anbieterunabhängige Heuristik zur Erkennung typischer Hörspiel-Albumstrukturen.
    /// Die Analyse basiert ausschließlich auf Trackanzahl und Trackdauer, da sich Hörspiele
    /// strukturell deutlich von Musikalben unterscheiden.
    /// </summary>
    /// <remarks>
    /// Eine Hörspielfolge läuft 40 bis 80 Minuten und ist in wenige lange Kapitel geteilt; ein
    /// Musikalbum hat zehn bis zwanzig Stücke von drei bis fünf Minuten. Die Schwellen liegen
    /// dazwischen: ein Kapitel ab zehn Minuten, im Mittel mindestens fünf Minuten, höchstens
    /// zwanzig Spuren. Folgen, die beim Anbieter in viele kurze Spuren zerlegt sind, fallen
    /// damit heraus; für sie zählen die übrigen Merkmale der Bewertung.
    /// </remarks>
    public static class HoerspielAlbumHeuristic
    {
        /// <summary>
        /// Mindestdauer eines einzelnen Tracks, um als Hörspiel-Track zu gelten.
        /// </summary>
        private static readonly TimeSpan MinSingleTrackDuration = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Mindestdurchschnittsdauer der Tracks eines Albums, um als Hörspiel-Album zu gelten.
        /// </summary>
        private static readonly TimeSpan MinAverageTrackDuration = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Maximale Trackanzahl für ein typisches Hörspiel-Album.
        /// </summary>
        private const int MaxTracks = 20;

        /// <summary>
        /// Prüft anhand von Trackdauern, ob ein Album die typischen Merkmale eines Hörspiels aufweist.
        /// </summary>
        /// <param name="durations">Die Dauern der einzelnen Tracks.</param>
        /// <returns><c>true</c>, wenn die Trackstruktur einem Hörspiel-Muster entspricht.</returns>
        public static bool LooksLikeHoerspiel(IReadOnlyList<TimeSpan> durations)
        {
            ArgumentNullException.ThrowIfNull(durations);

            if (durations.Count == 0 || durations.Count > MaxTracks)
            {
                return false;
            }

            // Mindestens ein Track muss lang genug sein
            bool hasLongTrack = durations.Any(d => d >= MinSingleTrackDuration);

            // Durchschnittliche Trackdauer muss über dem Schwellenwert liegen
            TimeSpan averageDuration = TimeSpan.FromTicks(
                (long)durations.Average(d => d.Ticks));

            bool hasHighAverage = averageDuration >= MinAverageTrackDuration;

            return hasLongTrack && hasHighAverage;
        }
    }
}
