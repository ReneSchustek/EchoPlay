using EchoPlay.Core.Parsing;

namespace EchoPlay.Core.Models.Import
{
    /// <summary>
    /// Erzeugt <see cref="ImportEpisode"/>-Objekte aus provider-neutralen Bausteinen.
    /// Kapselt die driftgefährdete Logik (Dauer-Summierung, Episodennummer-Extraktion
    /// und den Objektaufbau), damit Spotify- und Apple-Music-Mapper nur ihre eigenen
    /// Felder extrahieren müssen.
    /// </summary>
    public static class ImportEpisodeFactory
    {
        /// <summary>
        /// Baut eine Import-Episode: summiert die Track-Dauern, leitet die Episodennummer
        /// aus dem Titel ab und setzt die übergebenen Felder.
        /// </summary>
        /// <param name="sourceEpisodeId">Die Quell-ID der Episode (Album-/Collection-ID).</param>
        /// <param name="title">Der Episodentitel.</param>
        /// <param name="releaseDate">Das Veröffentlichungsdatum, falls bekannt.</param>
        /// <param name="trackDurations">Die Einzeldauern der Tracks (werden aufsummiert).</param>
        /// <param name="orderIndex">Sortierposition innerhalb der Serie.</param>
        /// <param name="providerUrl">Die geprüfte Anbieter-Adresse der Folge; <see langword="null"/>, wenn die Antwort keine brauchbare lieferte.</param>
        /// <param name="coverImageUrl">Die geprüfte Cover-Adresse, falls vorhanden.</param>
        /// <param name="source">Der Quell-Bezeichner (z.B. "Spotify", "AppleMusic").</param>
        /// <returns>Die zusammengesetzte Import-Episode.</returns>
        public static ImportEpisode Create(
            string sourceEpisodeId,
            string title,
            DateTime? releaseDate,
            IEnumerable<TimeSpan> trackDurations,
            int orderIndex,
            Uri? providerUrl,
            Uri? coverImageUrl,
            string source)
        {
            ArgumentNullException.ThrowIfNull(sourceEpisodeId);
            ArgumentNullException.ThrowIfNull(title);
            ArgumentNullException.ThrowIfNull(trackDurations);
            ArgumentNullException.ThrowIfNull(source);

            TimeSpan totalDuration = TimeSpan.Zero;
            foreach (TimeSpan duration in trackDurations)
            {
                totalDuration += duration;
            }

            return new ImportEpisode
            {
                SourceEpisodeId = sourceEpisodeId,
                Title = title,
                EpisodeNumber = EpisodeNumberParser.Extract(title),
                ReleaseDate = releaseDate,
                Duration = totalDuration,
                OrderIndex = orderIndex,
                ProviderUrl = providerUrl,
                CoverImageUrl = coverImageUrl,
                Source = source
            };
        }
    }
}
