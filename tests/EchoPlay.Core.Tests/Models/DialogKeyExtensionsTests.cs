using EchoPlay.Core.Models;
using System;
using System.Linq;
using Xunit;

namespace EchoPlay.Core.Tests.Models
{
    /// <summary>
    /// Prüft, welche Rückfragen als „ohne Rückweg" gelten. Die Liste steuert einen
    /// Warnhinweis in den Einstellungen — sie zu weit oder zu eng zu fassen wäre beides
    /// eine falsche Auskunft an den Nutzer.
    /// </summary>
    public sealed class DialogKeyExtensionsTests
    {
        [Theory]
        [InlineData(DialogKey.TagManagerRename)]
        [InlineData(DialogKey.TagManagerSaveAll)]
        [InlineData(DialogKey.TagManagerRemoveAll)]
        [InlineData(DialogKey.TagManagerCoverApplyAll)]
        [InlineData(DialogKey.TagManagerApplyToAll)]
        public void TagManagerSchreibaktionen_HabenKeinenRueckweg(DialogKey key)
        {
            Assert.True(key.WritesFilesWithoutUndo());
        }

        [Theory]
        [InlineData(DialogKey.SeriesDeleteFromDisk)]
        [InlineData(DialogKey.SeriesRemoveFromLibrary)]
        [InlineData(DialogKey.LibraryReinit)]
        [InlineData(DialogKey.ImportFailed)]
        [InlineData(DialogKey.None)]
        public void AllesAndere_HatEinenRueckweg(DialogKey key)
        {
            // Löschen läuft in EchoPlay über Soft-Delete und bleibt bis zum Ablauf von
            // DbPurgeDays umkehrbar — deshalb gehört auch das Löschen von der Platte
            // nicht in die Warnliste.
            Assert.False(key.WritesFilesWithoutUndo());
        }

        [Fact]
        public void GenauFuenfDialogeSindOhneRueckweg()
        {
            int count = Enum.GetValues<DialogKey>().Count(key => key.WritesFilesWithoutUndo());

            Assert.Equal(5, count);
        }
    }
}
