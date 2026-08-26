namespace EchoPlay.AppleMusic.Scoring
{
    /// <summary>
    /// Prüft, ob das primäre Genre eines iTunes-Künstlers auf Hörspiel-Inhalte hinweist.
    /// </summary>
    /// <remarks>
    /// Steht als eigener Baustein, weil zwei Stellen dieselbe Frage stellen: der Vorfilter,
    /// bevor er einen Künstler zur teuren Albenprüfung zulässt, und die Analyse, wenn sie
    /// den Genre-Bonus vergibt.
    /// </remarks>
    internal static class AppleMusicGenreMatcher
    {
        /// <summary>
        /// Prüft das primäre Genre gegen die Liste der Hörspiel-Genres.
        /// </summary>
        /// <param name="primaryGenreName">Das primäre Genre aus der Anbieter-Antwort.</param>
        /// <param name="hoerspielGenres">Die als Hörspiel geltenden Genre-Bezeichnungen.</param>
        /// <returns><c>true</c>, wenn ein Hörspiel-typisches Genre erkannt wurde.</returns>
        public static bool IsHoerspielGenre(string? primaryGenreName, IEnumerable<string> hoerspielGenres)
        {
            ArgumentNullException.ThrowIfNull(hoerspielGenres);

            if (string.IsNullOrWhiteSpace(primaryGenreName))
            {
                return false;
            }

            foreach (string genre in hoerspielGenres)
            {
                if (string.Equals(primaryGenreName, genre, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
