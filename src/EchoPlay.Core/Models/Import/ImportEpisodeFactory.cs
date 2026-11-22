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
        /// <param name="draft">Die Felder der Folge, wie der Anbieter sie liefert.</param>
        /// <param name="trackDurations">Die Einzeldauern der Tracks (werden aufsummiert).</param>
        /// <returns>Die zusammengesetzte Import-Episode.</returns>
        public static ImportEpisode Create(
            ImportEpisodeDraft draft,
            IEnumerable<TimeSpan> trackDurations)
        {
            ArgumentNullException.ThrowIfNull(draft);
            ArgumentNullException.ThrowIfNull(draft.SourceEpisodeId);
            ArgumentNullException.ThrowIfNull(draft.Title);
            ArgumentNullException.ThrowIfNull(trackDurations);
            ArgumentNullException.ThrowIfNull(draft.Source);

            TimeSpan totalDuration = TimeSpan.Zero;
            foreach (TimeSpan duration in trackDurations)
            {
                totalDuration += duration;
            }

            return new ImportEpisode
            {
                SourceEpisodeId = draft.SourceEpisodeId,
                Title = draft.Title,
                EpisodeNumber = EpisodeNumberParser.Extract(draft.Title),
                ReleaseDate = draft.ReleaseDate,
                Duration = totalDuration,
                OrderIndex = draft.OrderIndex,
                ProviderUrl = draft.ProviderUrl,
                CoverImageUrl = draft.CoverImageUrl,
                Source = draft.Source
            };
        }
    }
}
