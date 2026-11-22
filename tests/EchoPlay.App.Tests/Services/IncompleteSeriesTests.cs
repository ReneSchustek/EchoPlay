using EchoPlay.App.Services;
using EchoPlay.App.Tests.Helpers;
using EchoPlay.Data.Entities.Common;
using EchoPlay.Data.Entities.Library;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft, welche lokalen Serien als unvollständig gelten: Lücke in der Nummernfolge oder
    /// eine neue Folge beim Online-Gegenstück. Dieselben Fälle meldet der Bericht „Fehlende Folgen".
    /// </summary>
    public sealed class IncompleteSeriesTests
    {
        private const string AppleArtist = "1194782821";
        private const string SpotifyArtist = "4mF2pM1xDSeyYk2lRo3Ytl";

        [Fact]
        public void Find_GapInNumbers_IsIncomplete()
        {
            Series local = Local(TestIds.SeriesA);

            IReadOnlySet<Guid> result = IncompleteSeries.Find([local], [LocalEpisode(local, 1), LocalEpisode(local, 3)]);

            Assert.Contains(local.Id, result);
        }

        [Fact]
        public void Find_ContinuousNumbers_IsComplete()
        {
            Series local = Local(TestIds.SeriesA);

            Assert.Empty(IncompleteSeries.Find([local], [LocalEpisode(local, 1), LocalEpisode(local, 2)]));
        }

        [Fact]
        public void Find_SpecialEpisodesWithoutNumber_DoNotCount()
        {
            // Ein Adventskalender ohne Nummer ist weder Lücke noch Beleg für eine.
            Series local = Local(TestIds.SeriesA);

            Assert.Empty(IncompleteSeries.Find([local], [LocalEpisode(local, 1), LocalEpisode(local, null), LocalEpisode(local, 2)]));
        }

        [Fact]
        public void Find_NewEpisodeAtAppleMusicTwin_IsIncomplete()
        {
            Series local = Local(TestIds.SeriesA, appleArtist: AppleArtist);
            Series twin = Online(TestIds.SeriesB, appleArtist: AppleArtist);

            IReadOnlySet<Guid> result = IncompleteSeries.Find(
                [local, twin],
                [LocalEpisode(local, 1), LocalEpisode(local, 2), OnlineEpisode(twin, 2), OnlineEpisode(twin, 3)]);

            Assert.Contains(local.Id, result);
        }

        [Fact]
        public void Find_NewEpisodeAtSpotifyTwin_IsIncomplete()
        {
            Series local = Local(TestIds.SeriesA, spotifyArtist: SpotifyArtist);
            Series twin = Online(TestIds.SeriesB, spotifyArtist: SpotifyArtist);

            Assert.Contains(local.Id, IncompleteSeries.Find([local, twin], [LocalEpisode(local, 1), OnlineEpisode(twin, 2)]));
        }

        [Fact]
        public void Find_TwinHasNothingAfterLocalHighest_IsComplete()
        {
            Series local = Local(TestIds.SeriesA, appleArtist: AppleArtist);
            Series twin = Online(TestIds.SeriesB, appleArtist: AppleArtist);

            Assert.Empty(IncompleteSeries.Find([local, twin], [LocalEpisode(local, 1), LocalEpisode(local, 2), OnlineEpisode(twin, 1)]));
        }

        [Fact]
        public void Find_LocalSeriesIsItselfTheImport_FilesCountAsPresent()
        {
            // Ist die lokale Serie selbst der Online-Import, stehen ihre vorhandenen Folgen in
            // derselben Liste; was Dateien hat, ist keine neue Folge.
            Series series = Local(TestIds.SeriesA, appleArtist: AppleArtist);
            series.IsOnlineImported = true;
            Episode present = LocalEpisode(series, 2);
            present.LocalTrackCount = 12;

            Assert.Empty(IncompleteSeries.Find([series], [LocalEpisode(series, 1), present]));
        }

        [Fact]
        public void Find_NoLocalNumbers_IsComplete()
        {
            // Ohne Nummer gibt es kein „danach"; jede Folge des Anbieters wäre sonst neu.
            Series local = Local(TestIds.SeriesA, appleArtist: AppleArtist);
            Series twin = Online(TestIds.SeriesB, appleArtist: AppleArtist);

            Assert.Empty(IncompleteSeries.Find([local, twin], [LocalEpisode(local, null), OnlineEpisode(twin, 5)]));
        }

        [Fact]
        public void Find_OnlySeriesWithLocalFolder_AreJudged()
        {
            Series twin = Online(TestIds.SeriesB, appleArtist: AppleArtist);

            Assert.Empty(IncompleteSeries.Find([twin], [OnlineEpisode(twin, 1), OnlineEpisode(twin, 3)]));
        }

        [Fact]
        public void SeriesToLoad_IncludesLocalSeriesAndTheirTwins()
        {
            Series local = Local(TestIds.SeriesA, appleArtist: AppleArtist);
            Series twin = Online(TestIds.SeriesB, appleArtist: AppleArtist);
            Series unrelated = Online(TestIds.SeriesC, appleArtist: "999");

            IReadOnlyList<Guid> ids = IncompleteSeries.SeriesToLoad([local, twin, unrelated]);

            Assert.Equal([local.Id, twin.Id], ids);
        }

        private static Series Local(Guid id, string? appleArtist = null, string? spotifyArtist = null)
            => WithId(new Series { Title = "Lokal", LocalFolderPath = @"D:\Hörspiele\Serie", AppleMusicArtistId = appleArtist, SpotifyArtistId = spotifyArtist }, id);

        private static Series Online(Guid id, string? appleArtist = null, string? spotifyArtist = null)
            => WithId(new Series { Title = "Online", IsOnlineImported = true, AppleMusicArtistId = appleArtist, SpotifyArtistId = spotifyArtist }, id);

        private static Episode LocalEpisode(Series series, int? number)
            => new() { SeriesId = series.Id, EpisodeNumber = number, LocalFolderPath = $@"D:\Hörspiele\Serie\{number}" };

        private static Episode OnlineEpisode(Series series, int number)
            => new() { SeriesId = series.Id, EpisodeNumber = number };

        private static Series WithId(Series series, Guid id)
        {
            typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(series, id);
            return series;
        }
    }
}
