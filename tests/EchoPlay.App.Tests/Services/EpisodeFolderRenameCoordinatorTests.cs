using EchoPlay.App.Services;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft, wie nach dem Umbenennen eines Folgenordners die Dateipfade der Spuren
    /// nachgezogen werden.
    /// </summary>
    public sealed class EpisodeFolderRenameCoordinatorTests
    {
        private const string OldFolder = @"D:\Hörspiele\Die drei Ausrufezeichen\Serie - 087 - Alt";
        private const string NewFolder = @"D:\Hörspiele\Die drei Ausrufezeichen\087 - Geheimnisvoller Liebestrank";

        [Fact]
        public void RelocatePath_FileInRenamedFolder_MovesWithIt()
        {
            string moved = EpisodeFolderRenameCoordinator.RelocatePath(OldFolder + @"\01 - Kapitel.mp3", OldFolder, NewFolder);

            Assert.Equal(NewFolder + @"\01 - Kapitel.mp3", moved);
        }

        [Fact]
        public void RelocatePath_DifferentCase_StillMoves()
        {
            // Windows unterscheidet bei Pfaden nicht nach Groß- und Kleinschreibung.
            string moved = EpisodeFolderRenameCoordinator.RelocatePath(OldFolder.ToUpperInvariant() + @"\01.mp3", OldFolder, NewFolder);

            Assert.Equal(NewFolder + @"\01.mp3", moved);
        }

        [Fact]
        public void RelocatePath_FolderWithSameStart_IsNotTouched()
        {
            // „…Alt 2" beginnt wie „…Alt", ist aber ein anderer Ordner.
            string other = OldFolder + @" 2\01.mp3";

            Assert.Equal(other, EpisodeFolderRenameCoordinator.RelocatePath(other, OldFolder, NewFolder));
        }

        [Fact]
        public void RelocatePath_OldFolderWithTrailingSeparator_MovesFile()
        {
            string moved = EpisodeFolderRenameCoordinator.RelocatePath(OldFolder + @"\01.mp3", OldFolder + @"\", NewFolder);

            Assert.Equal(NewFolder + @"\01.mp3", moved);
        }
    }
}
