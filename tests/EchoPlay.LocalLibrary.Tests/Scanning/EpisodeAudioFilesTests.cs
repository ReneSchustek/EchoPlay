using EchoPlay.LocalLibrary.Scanning;

namespace EchoPlay.LocalLibrary.Tests.Scanning
{
    /// <summary>
    /// Tests für <see cref="EpisodeAudioFiles"/>: welche Dateien zu einer Folge gehören, auch wenn
    /// sie auf mehreren CDs in Unterordnern liegen. Echte Ordner in einem eigenen Temp-Verzeichnis.
    /// </summary>
    public sealed class EpisodeAudioFilesTests : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("echoplay_audio_").FullName;

        /// <summary>Löscht das Temp-Verzeichnis samt Inhalt.</summary>
        public void Dispose() => Directory.Delete(_root, recursive: true);

        [Fact]
        public void Collect_FilesDirectlyInFolder_AreTakenAndSubfoldersIgnored()
        {
            string episode = Folder("001 - Folge");
            File("001 - Folge", "01.mp3");
            File("001 - Folge/Bonus", "99.mp3");

            Assert.Equal([Path.Combine(episode, "01.mp3")], EpisodeAudioFiles.Collect(episode));
        }

        [Fact]
        public void Collect_CdSubfolders_AreTakenInCdOrder()
        {
            // „075 - Tatort Hollywood" der !!!: drei CDs, keine Datei direkt im Folgenordner.
            string episode = Folder("075 - Tatort Hollywood");
            File("075 - Tatort Hollywood/CD2 - Gefahr im Paradies", "01.mp3");
            File("075 - Tatort Hollywood/CD1 - Falscher Verdacht", "02.mp3");
            File("075 - Tatort Hollywood/CD1 - Falscher Verdacht", "01.mp3");

            IReadOnlyList<string> tracks = EpisodeAudioFiles.Collect(episode);

            Assert.Equal(
                ["CD1 - Falscher Verdacht/01.mp3", "CD1 - Falscher Verdacht/02.mp3", "CD2 - Gefahr im Paradies/01.mp3"],
                tracks.Select(track => Path.GetRelativePath(episode, track).Replace('\\', '/')));
        }

        [Fact]
        public void Collect_PartFoldersWithSameNumber_AreTaken()
        {
            // „100 - Toteninsel" der ???: Teile „100 A", „100 B" tragen die Nummer der Folge.
            string episode = Folder("100 - Toteninsel");
            File("100 - Toteninsel/100 A - Das Rätsel der Sphinx", "01.mp3");
            File("100 - Toteninsel/100 B - Das vergessene Volk", "01.mp3");

            Assert.Equal(2, EpisodeAudioFiles.Collect(episode).Count);
        }

        [Fact]
        public void Collect_NumberedPartFolders_AreTaken()
        {
            // „150 - Geisterbucht" der ???: Teile „01 (A) …", „02 (B) …" zählen von vorn.
            string episode = Folder("150 - Geisterbucht");
            File("150 - Geisterbucht/01 (A) Rashuras Schatz", "01.mp3");
            File("150 - Geisterbucht/02 (B) Flammende Wasser", "01.mp3");
            File("150 - Geisterbucht/03 (C) Der brennende Kristall", "01.mp3");

            Assert.Equal(3, EpisodeAudioFiles.Collect(episode).Count);
        }

        [Fact]
        public void Collect_SubfolderWithOtherNumber_IsAnOwnEpisodeAndSkipped()
        {
            string episode = Folder("010 - Sammelbox");
            File("010 - Sammelbox/011 - Eigene Folge", "01.mp3");

            Assert.Empty(EpisodeAudioFiles.Collect(episode));
        }

        [Fact]
        public void Collect_DeeperThanTwoLevels_IsIgnored()
        {
            string episode = Folder("012 - Box");
            File("012 - Box/Teil 1/CD 1/Extra", "01.mp3");

            Assert.Empty(EpisodeAudioFiles.Collect(episode));
        }

        [Fact]
        public void HasAudio_EmptyFolder_IsFalse()
        {
            Assert.False(EpisodeAudioFiles.HasAudio(Folder("013 - Leer")));
        }

        [Fact]
        public void HasAudio_MissingFolder_IsFalse()
        {
            Assert.False(EpisodeAudioFiles.HasAudio(Path.Combine(_root, "gibt es nicht")));
        }

        [Fact]
        public void FolderCount_TracksFromTwoCds_IsTwo()
        {
            Assert.Equal(2, EpisodeAudioFiles.FolderCount([@"D:\F\CD1\01.mp3", @"D:\F\CD1\02.mp3", @"D:\F\CD2\01.mp3"]));
        }

        private string Folder(string relative) => Directory.CreateDirectory(Path.Combine(_root, relative)).FullName;

        private void File(string folder, string name)
        {
            string path = Path.Combine(Folder(folder), name);
            System.IO.File.WriteAllText(path, string.Empty);
        }
    }
}
