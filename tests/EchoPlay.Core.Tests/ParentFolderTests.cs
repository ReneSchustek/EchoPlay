namespace EchoPlay.Core.Tests
{
    /// <summary>Tests für <see cref="ParentFolder"/>.</summary>
    public sealed class ParentFolderTests
    {
        [Fact]
        public void Of_FilePath_ReturnsItsFolder()
        {
            Assert.Equal(@"C:\Daten\EchoPlay", ParentFolder.Of(@"C:\Daten\EchoPlay\echoplay.db"));
        }

        [Fact]
        public void Of_DriveRoot_Throws()
        {
            // Eine Laufwerkswurzel hat keinen übergeordneten Ordner; still mit null weiterzurechnen
            // hieße, später einen Pfad aus dem Nichts zu bilden.
            _ = Assert.Throws<ArgumentException>(() => ParentFolder.Of(@"C:\"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Of_EmptyPath_Throws(string path)
        {
            _ = Assert.Throws<ArgumentException>(() => ParentFolder.Of(path));
        }
    }
}
