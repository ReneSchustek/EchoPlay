using EchoPlay.App.ViewModels;
using EchoPlay.LocalLibrary.Models;

namespace EchoPlay.App.Tests.ViewModels
{
    /// <summary>
    /// Prüft den Text, den der Dialog „Ordner nach Tags umbenennen" zeigt. Im Testhost fehlen die
    /// App-Ressourcen; es gelten die deutschen Vorgaben im Code.
    /// </summary>
    public sealed class FolderRenamePreviewTextTests
    {
        private const string Series = @"D:\Hörspiele\Die drei Ausrufezeichen";

        [Fact]
        public void Build_ListsRenamesAndExplainsWhatStays()
        {
            FolderRenamePlan plan = Plan(
                new FolderRenameItem(Series + @"\Serie - 087 - Alt", "087 - Geheimnisvoller Liebestrank", FolderRenameStatus.Rename, TitleFromTag: true),
                new FolderRenameItem(Series + @"\Serie - XXX - Adventskalender", "Serie - XXX - Adventskalender", FolderRenameStatus.NoNumber, TitleFromTag: false),
                new FolderRenameItem(Series + @"\118.1 - Teil 1", "118 - Das Tal der Angst", FolderRenameStatus.Conflict, TitleFromTag: true));

            string text = FolderRenamePreviewText.Build(plan);

            Assert.Contains("1 Ordner werden umbenannt", text, StringComparison.Ordinal);
            Assert.Contains("Serie - 087 - Alt", text, StringComparison.Ordinal);
            Assert.Contains("\u2192 087 - Geheimnisvoller Liebestrank", text, StringComparison.Ordinal);
            Assert.Contains("Sonderfolge", text, StringComparison.Ordinal);
            Assert.Contains("Serie - XXX - Adventskalender", text, StringComparison.Ordinal);
            Assert.Contains("Ziel schon vergeben", text, StringComparison.Ordinal);
        }

        [Fact]
        public void Build_TitleFromFolderName_IsMarked()
        {
            // Ohne Album-Tag stammt der Titel aus dem Ordnernamen; das soll sichtbar sein.
            FolderRenamePlan plan = Plan(new FolderRenameItem(Series + @"\Serie - 088 - Krimi", "088 - Krimi", FolderRenameStatus.Rename, TitleFromTag: false));

            Assert.Contains("Titel aus dem Ordnernamen", FolderRenamePreviewText.Build(plan), StringComparison.Ordinal);
        }

        [Fact]
        public void Build_NothingToRename_SaysSo()
        {
            FolderRenamePlan plan = Plan(new FolderRenameItem(Series + @"\087 - Liebestrank", "087 - Liebestrank", FolderRenameStatus.Unchanged, TitleFromTag: true));

            Assert.Contains("schon nach dem Muster", FolderRenamePreviewText.Build(plan), StringComparison.Ordinal);
        }

        [Fact]
        public void Build_UnnumberedSeries_ExplainsWhyNothingHappens()
        {
            FolderRenamePlan plan = new() { SeriesFolderPath = Series, Items = [], SeriesIsNumbered = false };

            Assert.Contains("trägt eine Folgennummer", FolderRenamePreviewText.Build(plan), StringComparison.Ordinal);
        }

        [Fact]
        public void Build_UnsupportedPattern_NamesTheAllowedPlaceholders()
        {
            FolderRenamePlan plan = new() { SeriesFolderPath = Series, Items = [], PatternSupported = false };

            Assert.Contains("{number:000}", FolderRenamePreviewText.Build(plan), StringComparison.Ordinal);
        }

        [Fact]
        public void Build_RecoveredJournal_IsReportedFirst()
        {
            FolderRenamePlan plan = new() { SeriesFolderPath = Series, Items = [], RecoveredCount = 2 };

            Assert.StartsWith("Ein abgebrochener Lauf wurde zurückgenommen: 2", FolderRenamePreviewText.Build(plan), StringComparison.Ordinal);
        }

        [Fact]
        public void BuildResult_WithSkipped_NamesBoth()
        {
            FolderRenameResult result = new([(Series + @"\a", Series + @"\b")], SkippedCount: 1);

            string text = FolderRenamePreviewText.BuildResult(result);

            Assert.Contains("1 Ordner umbenannt", text, StringComparison.Ordinal);
            Assert.Contains("1 Ordner übersprungen", text, StringComparison.Ordinal);
        }

        [Fact]
        public void BuildResult_NothingSkipped_OnlyCountsRenames()
        {
            FolderRenameResult result = new([], SkippedCount: 0);

            Assert.Equal("0 Ordner umbenannt.", FolderRenamePreviewText.BuildResult(result));
        }

        private static FolderRenamePlan Plan(params FolderRenameItem[] items) => new() { SeriesFolderPath = Series, Items = items };
    }
}
