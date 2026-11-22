using EchoPlay.TagManager.Models;
using EchoPlay.TagManager.Services;
using EchoPlay.TagManager.Tests.Fakes;
using EchoPlay.TagManager.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace EchoPlay.TagManager.Tests.Services
{
    /// <summary>
    /// Prüft das Schreiben aller Kennzeichnungsfelder und die Wege, auf denen eine
    /// Datei nicht gelesen oder geschrieben werden kann.
    /// </summary>
    /// <remarks>
    /// Der Kennzeichnungs-Verwalter arbeitet auf den Dateien des Anwenders. Ein
    /// Fehlschlag muss beim Aufrufer ankommen, denn still zu scheitern hieße, dass der
    /// Anwender seine Eingaben für gespeichert hält, während nichts auf der Platte steht.
    /// Beim Lesen eines ganzen Ordners gilt das Gegenteil: Eine kaputte Datei darf die
    /// übrigen nicht mitnehmen.
    ///
    /// Die Tests arbeiten auf echten Dateien im Temp-Verzeichnis.
    /// </remarks>
    public sealed class TagServiceAllFieldsTests
    {
        /// <summary>Ein Tag, in dem jedes Feld belegt ist, das der Verwalter schreiben kann.</summary>
        private static AudioTag EveryFieldSet()
            => new()
            {
                Title = "Der Fall des Jahres",
                Album = "TKKG Folge 250",
                Artist = "TKKG",
                AlbumArtist = "Europa",
                Comment = "Eigene Aufnahme",
                Genre = "Hörspiel",
                Year = 2026,
                TrackNumber = 3,
                TrackCount = 12,
                DiscNumber = 1,
                DiscCount = 2,
                CoverImageData = [0x01, 0x02, 0x03],
                CoverMimeType = "image/png",
            };

        [Fact]
        public async Task Write_WithEveryField_StoresThemAll()
        {
            string path = AudioTestFileFactory.CreateTempMp3();
            try
            {
                TagService sut = new(new FakeLoggerFactory());
                await sut.WriteAsync(path, EveryFieldSet());
                AudioTag readBack = await sut.ReadAsync(path);

                // Der Kennzeichnungs-Verwalter ist der einzige Weg, auf dem der Anwender
                // eigene Aufnahmen benennt. Fällt ein Feld dabei unter den Tisch, merkt
                // er es erst, wenn die Mediathek die Folge falsch einsortiert.
                Assert.Equal("Der Fall des Jahres", readBack.Title);
                Assert.Equal("TKKG Folge 250", readBack.Album);
                Assert.Equal("TKKG", readBack.Artist);
                Assert.Equal("Europa", readBack.AlbumArtist);
                Assert.Equal("Eigene Aufnahme", readBack.Comment);
                Assert.Equal("Hörspiel", readBack.Genre);
                Assert.Equal(2026u, readBack.Year);
                Assert.Equal(3u, readBack.TrackNumber);
                Assert.Equal(12u, readBack.TrackCount);
                Assert.Equal(1u, readBack.DiscNumber);
                Assert.Equal(2u, readBack.DiscCount);
                Assert.NotNull(readBack.CoverImageData);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task Write_ToAFileThatIsNotThere_Throws()
        {
            TagService sut = new(new FakeLoggerFactory());
            string missing = Path.Combine(Path.GetTempPath(), $"gibt-es-nicht-{Path.GetRandomFileName()}.mp3");

            _ = await Assert.ThrowsAnyAsync<Exception>(
                () => sut.WriteAsync(missing, new AudioTag { Title = "egal" }));
        }

        [Fact]
        public async Task WriteCover_ToAFileThatIsNotThere_Throws()
        {
            TagService sut = new(new FakeLoggerFactory());
            string missing = Path.Combine(Path.GetTempPath(), $"gibt-es-nicht-{Path.GetRandomFileName()}.mp3");

            _ = await Assert.ThrowsAnyAsync<Exception>(
                () => sut.WriteCoverAsync(missing, [0x01, 0x02]));
        }

        [Fact]
        public async Task RemoveAllTags_FromAFileThatIsNotThere_Throws()
        {
            TagService sut = new(new FakeLoggerFactory());
            string missing = Path.Combine(Path.GetTempPath(), $"gibt-es-nicht-{Path.GetRandomFileName()}.mp3");

            _ = await Assert.ThrowsAnyAsync<Exception>(() => sut.RemoveAllTagsAsync(missing));
        }

        [Fact]
        public async Task ReadFolder_ForAFolderThatIsNotThere_Throws()
        {
            TagService sut = new(new FakeLoggerFactory());
            string missing = Path.Combine(Path.GetTempPath(), $"gibt-es-nicht-{Path.GetRandomFileName()}");

            _ = await Assert.ThrowsAsync<DirectoryNotFoundException>(() => sut.ReadFolderAsync(missing));
        }

        [Fact]
        public async Task ReadFolder_WithOneBrokenFile_ReadsTheRest()
        {
            string folder = Path.Combine(Path.GetTempPath(), $"echoplay-tags-{Path.GetRandomFileName()}");
            _ = Directory.CreateDirectory(folder);
            try
            {
                string good = AudioTestFileFactory.CreateTempMp3();
                File.Move(good, Path.Combine(folder, "01 - Gut.mp3"));

                // Eine Textdatei mit Audio-Endung: Genau so sieht ein abgebrochener
                // Download aus.
                await File.WriteAllTextAsync(
                    Path.Combine(folder, "02 - Kaputt.mp3"), "das ist kein Ton",
                    TestContext.Current.CancellationToken);

                TagService sut = new(new FakeLoggerFactory());

                IReadOnlyList<(string FilePath, AudioTag Tag)> readBack = await sut.ReadFolderAsync(folder);

                // Eine kaputte Datei kostet ihre eigene Zeile, nicht den ganzen Ordner.
                (string FilePath, AudioTag Tag) einzig = Assert.Single(readBack);
                Assert.EndsWith("01 - Gut.mp3", einzig.FilePath, StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
