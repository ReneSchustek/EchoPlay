using EchoPlay.App.Services;
using EchoPlay.Core.Models.Import;
using EchoPlay.Data.Entities.Library;
using System;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Sichert die Wiedererkennung bestehender Folgen ab. Sie entscheidet, ob ein Import
    /// eine Folge anlegt oder stehen lässt, und damit, ob der Bestand doppelt wird.
    /// </summary>
    public sealed class ExistingEpisodeIndexTests
    {
        [Fact]
        public void TryFind_RecognizesEpisodeByAlbumId_DespiteChangedTitle()
        {
            ExistingEpisodeIndex index = new(
            [
                new Episode { SeriesId = Guid.NewGuid(), Title = "Folge 240", AppleMusicAlbumId = "am240" }
            ]);

            bool found = index.TryFind(
                new ImportEpisode { SourceEpisodeId = "am240", Title = "Folge 240: Das verschollene Zepter" },
                out Episode? existing);

            Assert.True(found);
            Assert.Equal("Folge 240", existing!.Title);
        }

        [Fact]
        public void TryFind_RecognizesEpisodeByTitle_WhenNoIdIsStored()
        {
            // Lokal eingelesene Folgen haben keine Album-Kennung, für sie bleibt der Titel
            // der einzige Anker.
            ExistingEpisodeIndex index = new(
            [
                new Episode { SeriesId = Guid.NewGuid(), Title = "Folge 12" }
            ]);

            Assert.True(index.Contains(new ImportEpisode { SourceEpisodeId = "am12", Title = "folge 12" }));
        }

        [Fact]
        public void Contains_ReportsUnknownEpisodeAsNew()
        {
            ExistingEpisodeIndex index = new(
            [
                new Episode { SeriesId = Guid.NewGuid(), Title = "Folge 1", AppleMusicAlbumId = "am1" }
            ]);

            Assert.False(index.Contains(new ImportEpisode { SourceEpisodeId = "am2", Title = "Folge 2" }));
        }

        [Fact]
        public void Add_GuardsAgainstDuplicatesWithinOneProviderResponse()
        {
            ExistingEpisodeIndex index = new([]);
            ImportEpisode hit = new() { SourceEpisodeId = "am7", Title = "Folge 7" };

            Assert.False(index.Contains(hit));

            index.Add(new Episode { SeriesId = Guid.NewGuid(), Title = "Folge 7", AppleMusicAlbumId = "am7" });

            Assert.True(index.Contains(hit));
        }

        [Fact]
        public void Titles_ReturnsTheKnownTitlesForTheProviderHint()
        {
            ExistingEpisodeIndex index = new(
            [
                new Episode { SeriesId = Guid.NewGuid(), Title = "Folge 1" },
                new Episode { SeriesId = Guid.NewGuid(), Title = "Folge 2" }
            ]);

            Assert.Equal(2, index.Titles.Count);

            // Der Anbieter vergleicht die Albumnamen ohne Rücksicht auf Groß- und Kleinschreibung.
            Assert.True(index.Titles.Contains("folge 1"));
        }
    }
}
