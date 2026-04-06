using EchoPlay.TagManager.Models;
using EchoPlay.TagManager.Services;
using EchoPlay.TagManager.Tests.Fakes;
using EchoPlay.TagManager.Tests.Infrastructure;

namespace EchoPlay.TagManager.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="FileRenameService"/>.
    /// <para>
    /// Der Service baut eine Vorschau-Liste auf (<see cref="FileRenameService.BuildPreview"/>)
    /// und benennt Dateien tatsächlich um (<see cref="FileRenameService.RenameAsync"/>).
    /// Die Tests für <c>RenameAsync</c> arbeiten mit echten temporären MP3-Dateien, damit
    /// das File.Move-Verhalten geprüft werden kann – ohne Mocks oder Fake-Dateisysteme.
    /// </para>
    /// </summary>
    public sealed class FileRenameServiceTests
    {
        private static FileRenameService CreateService() => new(new FakeLoggerFactory());

        // ── BuildPreview ─────────────────────────────────────────────────────────

        [Fact]
        public void BuildPreview_ResolvesTitle()
        {
            FileRenameService service = CreateService();

            AudioTag tag = new() { Title = "Klassenfahrt zur Hexenburg" };
            IReadOnlyList<RenamePreviewItem> preview = service.BuildPreview(
                [(@"C:\audio\001.mp3", tag)],
                "{title}");

            Assert.Equal("Klassenfahrt zur Hexenburg.mp3", preview[0].NewName);
        }

        [Fact]
        public void BuildPreview_ResolvesAlbum()
        {
            FileRenameService service = CreateService();

            AudioTag tag = new() { Album = "TKKG" };
            IReadOnlyList<RenamePreviewItem> preview = service.BuildPreview(
                [(@"C:\audio\001.mp3", tag)],
                "{album}");

            Assert.Equal("TKKG.mp3", preview[0].NewName);
        }

        [Fact]
        public void BuildPreview_ResolvesArtist()
        {
            FileRenameService service = CreateService();

            AudioTag tag = new() { Artist = "EUROPA" };
            IReadOnlyList<RenamePreviewItem> preview = service.BuildPreview(
                [(@"C:\audio\001.mp3", tag)],
                "{artist}");

            Assert.Equal("EUROPA.mp3", preview[0].NewName);
        }

        [Fact]
        public void BuildPreview_ResolvesYear()
        {
            FileRenameService service = CreateService();

            AudioTag tag = new() { Year = 1991 };
            IReadOnlyList<RenamePreviewItem> preview = service.BuildPreview(
                [(@"C:\audio\001.mp3", tag)],
                "{year}");

            Assert.Equal("1991.mp3", preview[0].NewName);
        }

        [Fact]
        public void BuildPreview_ResolvesFilename()
        {
            FileRenameService service = CreateService();

            // {filename} soll den aktuellen Dateinamen ohne Extension einfügen –
            // nützlich wenn der Dateiname bereits sinnvoll ist und nur ergänzt werden soll
            AudioTag tag = new();
            IReadOnlyList<RenamePreviewItem> preview = service.BuildPreview(
                [(@"C:\audio\001 - Titel.mp3", tag)],
                "{filename}");

            Assert.Equal("001 - Titel.mp3", preview[0].NewName);
        }

        [Fact]
        public void BuildPreview_ResolvesTrackWithoutFormat()
        {
            FileRenameService service = CreateService();

            AudioTag tag = new() { TrackNumber = 5 };
            IReadOnlyList<RenamePreviewItem> preview = service.BuildPreview(
                [(@"C:\audio\file.mp3", tag)],
                "{track}");

            Assert.Equal("5.mp3", preview[0].NewName);
        }

        [Fact]
        public void BuildPreview_ResolvesTrackWithTwoDigitFormat()
        {
            FileRenameService service = CreateService();

            AudioTag tag = new() { TrackNumber = 5 };
            IReadOnlyList<RenamePreviewItem> preview = service.BuildPreview(
                [(@"C:\audio\file.mp3", tag)],
                "{track:00}");

            // Führende Null, damit die Sortierung auch bei mehr als 9 Tracks korrekt bleibt
            Assert.Equal("05.mp3", preview[0].NewName);
        }

        [Fact]
        public void BuildPreview_ResolvesTrackWithThreeDigitFormat()
        {
            FileRenameService service = CreateService();

            AudioTag tag = new() { TrackNumber = 5 };
            IReadOnlyList<RenamePreviewItem> preview = service.BuildPreview(
                [(@"C:\audio\file.mp3", tag)],
                "{track:000}");

            Assert.Equal("005.mp3", preview[0].NewName);
        }

        [Fact]
        public void BuildPreview_ReplacesTrackWithEmptyStringWhenNotSet()
        {
            FileRenameService service = CreateService();

            AudioTag tag = new() { TrackNumber = null };
            IReadOnlyList<RenamePreviewItem> preview = service.BuildPreview(
                [(@"C:\audio\file.mp3", tag)],
                "{track:00} - {title}");

            // SanitizeFileName trimmt führende/abschließende Leerzeichen,
            // daher entsteht "-" statt " - " (Track und Titel beide leer)
            Assert.Equal("-.mp3", preview[0].NewName);
        }

        [Fact]
        public void BuildPreview_CombinesMultiplePlaceholders()
        {
            FileRenameService service = CreateService();

            AudioTag tag = new()
            {
                TrackNumber = 7,
                Title       = "Das Geheimnis der Pyramide",
                Album       = "Die drei ???",
                Year        = 1982
            };
            IReadOnlyList<RenamePreviewItem> preview = service.BuildPreview(
                [(@"C:\audio\007.mp3", tag)],
                "{track:00} - {title}");

            Assert.Equal("07 - Das Geheimnis der Pyramide.mp3", preview[0].NewName);
        }

        [Fact]
        public void BuildPreview_SanitizesInvalidFileNameChars()
        {
            FileRenameService service = CreateService();

            // Doppelpunkt ist in Windows-Dateinamen verboten – er kommt in Serientiteln häufig vor
            AudioTag tag = new() { Title = "Folge 1: Der Anfang" };
            IReadOnlyList<RenamePreviewItem> preview = service.BuildPreview(
                [(@"C:\audio\file.mp3", tag)],
                "{title}");

            Assert.Equal("Folge 1_ Der Anfang.mp3", preview[0].NewName);
        }

        [Fact]
        public void BuildPreview_SetsIsUnchangedWhenPatternMatchesCurrentName()
        {
            FileRenameService service = CreateService();

            // Wenn alter und neuer Name identisch sind, soll IsUnchanged true sein –
            // RenameAsync überspringt diese Einträge dann ohne File.Move-Aufruf
            AudioTag tag = new() { Title = "Unverändert" };
            IReadOnlyList<RenamePreviewItem> preview = service.BuildPreview(
                [(@"C:\audio\Unverändert.mp3", tag)],
                "{title}");

            Assert.True(preview[0].IsUnchanged);
        }

        [Fact]
        public void BuildPreview_ReturnsEmptyListForEmptyInput()
        {
            FileRenameService service = CreateService();

            IReadOnlyList<RenamePreviewItem> preview = service.BuildPreview([], "{title}");

            Assert.Empty(preview);
        }

        [Fact]
        public void BuildPreview_PreservesExtension()
        {
            FileRenameService service = CreateService();

            AudioTag tag = new() { Title = "Track" };
            IReadOnlyList<RenamePreviewItem> preview = service.BuildPreview(
                [(@"C:\audio\file.flac", tag)],
                "{title}");

            Assert.EndsWith(".flac", preview[0].NewName);
        }

        [Fact]
        public void BuildPreview_SetsCorrectFilePaths()
        {
            FileRenameService service = CreateService();

            AudioTag tag = new() { Title = "Neuer Titel" };
            IReadOnlyList<RenamePreviewItem> preview = service.BuildPreview(
                [(@"C:\audio\alt.mp3", tag)],
                "{title}");

            // Verzeichnis bleibt gleich, nur der Dateiname ändert sich
            Assert.Equal(@"C:\audio\alt.mp3",        preview[0].FilePath);
            Assert.Equal(@"C:\audio\Neuer Titel.mp3", preview[0].NewFilePath);
        }

        // ── RenameAsync ──────────────────────────────────────────────────────────

        [Fact]
        public async Task RenameAsync_RenamesFileAndReturnsCount()
        {
            string originalPath = AudioTestFileFactory.CreateTempMp3();
            string expectedPath = Path.Combine(
                Path.GetDirectoryName(originalPath)!,
                "Geister.mp3");

            try
            {
                FileRenameService service = CreateService();
                AudioTag tag = new() { Title = "Geister" };

                int count = await service.RenameAsync([(originalPath, tag)], "{title}");

                Assert.Equal(1, count);
                Assert.True(File.Exists(expectedPath));
                Assert.False(File.Exists(originalPath));
            }
            finally
            {
                File.Delete(expectedPath);
            }
        }

        [Fact]
        public async Task RenameAsync_SkipsUnchangedFilesAndDoesNotCountThem()
        {
            string path = AudioTestFileFactory.CreateTempMp3();
            string originalFileName = Path.GetFileNameWithoutExtension(path);

            try
            {
                FileRenameService service = CreateService();

                // {filename} löst zum aktuellen Dateinamen auf → keine Änderung
                int count = await service.RenameAsync([(path, new AudioTag())], "{filename}");

                Assert.Equal(0, count);
                Assert.True(File.Exists(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task RenameAsync_SkipsLockedFileAndContinuesWithRest()
        {
            // Zwei Dateien: die erste wird gesperrt (FileShare.None), die zweite soll trotzdem umbenannt werden
            string lockedPath  = AudioTestFileFactory.CreateTempMp3();
            string normalPath  = AudioTestFileFactory.CreateTempMp3();
            string expectedPath = Path.Combine(
                Path.GetDirectoryName(normalPath)!,
                "Normal.mp3");

            try
            {
                using FileStream lockStream = new(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

                FileRenameService service = CreateService();

                AudioTag lockedTag = new() { Title = "Gesperrt" };
                AudioTag normalTag = new() { Title = "Normal" };

                int count = await service.RenameAsync(
                    [(lockedPath, lockedTag), (normalPath, normalTag)],
                    "{title}");

                // Nur die entsperrte Datei wurde erfolgreich umbenannt
                Assert.Equal(1, count);
                Assert.True(File.Exists(expectedPath));
            }
            finally
            {
                File.Delete(lockedPath);
                File.Delete(expectedPath);
            }
        }
    }
}
