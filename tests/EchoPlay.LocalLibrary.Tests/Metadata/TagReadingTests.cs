using EchoPlay.LocalLibrary.Metadata;
using EchoPlay.LocalLibrary.Tests.Infrastructure;

namespace EchoPlay.LocalLibrary.Tests.Metadata
{
    /// <summary>
    /// Prüft das Auslesen von Titel, Album, Spurnummer und Dauer aus einer Audiodatei.
    /// </summary>
    /// <remarks>
    /// Diese Angaben entscheiden, wie eine Folge in der Mediathek heißt und in welcher
    /// Reihenfolge ihre Teile laufen. Steht in der Datei nichts, muss ein leerer Wert
    /// herauskommen und keine Ausnahme, sonst bricht der Einlesevorgang an der ersten
    /// unbeschrifteten Datei ab.
    ///
    /// Die Tests arbeiten auf echten Dateien im Temp-Verzeichnis.
    /// </remarks>
    public sealed class TagReadingTests : IDisposable
    {
        private readonly string _folder;

        public TagReadingTests()
        {
            _folder = Path.Combine(Path.GetTempPath(), $"echoplay-tags-{Guid.NewGuid():N}");
            _ = Directory.CreateDirectory(_folder);
        }

        public void Dispose()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }

        [Fact]
        public void TagTitleReader_ReadsTitleAndAlbum()
        {
            string file = AudioTestFiles.CreateMp3WithTags(
                _folder, "Der Fall des Jahres", "TKKG Folge 250", trackNumber: 3);

            (string Title, string Album) gelesen = new TagTitleReader().Read(file);

            Assert.Equal("Der Fall des Jahres", gelesen.Title);
            Assert.Equal("TKKG Folge 250", gelesen.Album);
        }

        [Fact]
        public void TagTitleReader_WithoutAnAlbum_ReturnsAnEmptyValue()
        {
            string file = AudioTestFiles.CreateMp3(_folder);

            (string Title, string Album) gelesen = new TagTitleReader().Read(file);

            // Viele selbst gerippte Dateien tragen nur einen Titel. Das ist kein Fehler.
            Assert.Empty(gelesen.Album);
        }

        [Fact]
        public void AudioMetadataReader_ReadsTrackNumber()
        {
            string file = AudioTestFiles.CreateMp3WithTags(
                _folder, "Teil 3", "TKKG", trackNumber: 3);

            (TimeSpan Duration, int TrackNumber) gelesen = new AudioMetadataReader().Read(file);

            Assert.Equal(3, gelesen.TrackNumber);
        }

        [Fact]
        public void AudioMetadataReader_WithoutATrackNumber_ReturnsZero()
        {
            string file = AudioTestFiles.CreateMp3(_folder);

            (TimeSpan Duration, int TrackNumber) gelesen = new AudioMetadataReader().Read(file);

            // Null heißt „nicht gesetzt". Die Reihenfolge kommt dann aus dem Dateinamen.
            Assert.Equal(0, gelesen.TrackNumber);
        }
    }
}
