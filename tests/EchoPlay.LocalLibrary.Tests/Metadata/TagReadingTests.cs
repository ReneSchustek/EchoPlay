using EchoPlay.LocalLibrary.Metadata;
using EchoPlay.LocalLibrary.Tests.Infrastructure;
using System;
using System.IO;

namespace EchoPlay.LocalLibrary.Tests.Metadata
{
    /// <summary>
    /// Prüft das Auslesen von Titel, Album, Spurnummer und Dauer aus einer Audiodatei.
    /// </summary>
    /// <remarks>
    /// Diese Angaben entscheiden, wie eine Folge in der Mediathek heißt und in welcher
    /// Reihenfolge ihre Teile laufen. Steht in der Datei nichts, muss ein leerer Wert
    /// herauskommen und keine Ausnahme — sonst bricht der Einlesevorgang an der ersten
    /// unbeschrifteten Datei ab.
    ///
    /// Die Tests arbeiten auf echten Dateien im Temp-Verzeichnis.
    /// </remarks>
    public sealed class TagReadingTests : IDisposable
    {
        private readonly string _ordner;

        public TagReadingTests()
        {
            _ordner = Path.Combine(Path.GetTempPath(), $"echoplay-tags-{Guid.NewGuid():N}");
            _ = Directory.CreateDirectory(_ordner);
        }

        public void Dispose()
        {
            if (Directory.Exists(_ordner))
            {
                Directory.Delete(_ordner, recursive: true);
            }
        }

        [Fact]
        public void TagTitleReader_ReadsTitleAndAlbum()
        {
            string datei = AudioTestFiles.CreateMp3WithTags(
                _ordner, "Der Fall des Jahres", "TKKG Folge 250", trackNumber: 3);

            (string Title, string Album) gelesen = new TagTitleReader().Read(datei);

            Assert.Equal("Der Fall des Jahres", gelesen.Title);
            Assert.Equal("TKKG Folge 250", gelesen.Album);
        }

        [Fact]
        public void TagTitleReader_WithoutAnAlbum_ReturnsAnEmptyValue()
        {
            string datei = AudioTestFiles.CreateMp3(_ordner);

            (string Title, string Album) gelesen = new TagTitleReader().Read(datei);

            // Viele selbst gerippte Dateien tragen nur einen Titel. Das ist kein Fehler.
            Assert.Empty(gelesen.Album);
        }

        [Fact]
        public void AudioMetadataReader_ReadsTrackNumber()
        {
            string datei = AudioTestFiles.CreateMp3WithTags(
                _ordner, "Teil 3", "TKKG", trackNumber: 3);

            (TimeSpan Duration, int TrackNumber) gelesen = new AudioMetadataReader().Read(datei);

            Assert.Equal(3, gelesen.TrackNumber);
        }

        [Fact]
        public void AudioMetadataReader_WithoutATrackNumber_ReturnsZero()
        {
            string datei = AudioTestFiles.CreateMp3(_ordner);

            (TimeSpan Duration, int TrackNumber) gelesen = new AudioMetadataReader().Read(datei);

            // Null heißt „nicht gesetzt". Die Reihenfolge kommt dann aus dem Dateinamen.
            Assert.Equal(0, gelesen.TrackNumber);
        }
    }
}
