using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.LocalLibrary.Tests.Fakes;
using System;
using System.IO;

namespace EchoPlay.LocalLibrary.Tests.Scanning
{
    /// <summary>
    /// Prüft den Ordnerzugriff des Scans bei Ein- und Ausgabefehlern: Jede Methode liefert ein
    /// leeres Ergebnis und schreibt eine Warnung, statt den Scan abzubrechen.
    /// </summary>
    /// <remarks>
    /// Ein Dateipfad an Stelle eines Ordners löst denselben Ein- und Ausgabefehler aus wie ein
    /// abgerissenes Netzlaufwerk und lässt sich im Test zuverlässig herstellen.
    /// </remarks>
    public sealed class SafeDirectoryReaderTests : IDisposable
    {
        private readonly string _file;
        private readonly CapturingLoggerFactory _log = new();
        private readonly SafeDirectoryReader _reader;

        public SafeDirectoryReaderTests()
        {
            _file = Path.Combine(Directory.CreateTempSubdirectory("echoplay_reader_").FullName, "kein-ordner.mp3");
            File.WriteAllBytes(_file, []);
            _reader = new SafeDirectoryReader(_log.CreateLogger("Test"));
        }

        public void Dispose()
        {
            string? folder = Path.GetDirectoryName(_file);
            if (folder is not null && Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void CountAudioFiles_OnAFile_ReturnsZeroAndWarns()
        {
            Assert.Equal(0, _reader.CountAudioFiles(_file));
            Assert.Contains(_log.Entries, entry => entry.Level == "Warning");
        }

        [Fact]
        public void CollectTrackPaths_OnAFile_ReturnsNothingAndWarns()
        {
            Assert.Empty(_reader.CollectTrackPaths(_file));
            Assert.Contains(_log.Entries, entry => entry.Level == "Warning");
        }

        [Fact]
        public void GetSubdirectories_OnAFile_ReturnsNothingAndWarns()
        {
            Assert.Empty(_reader.GetSubdirectories(_file));
            Assert.Contains(_log.Entries, entry => entry.Level == "Warning");
        }
    }
}
