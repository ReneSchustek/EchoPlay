using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.LocalLibrary.Tests.Fakes;

namespace EchoPlay.LocalLibrary.Tests.Scanning
{
    /// <summary>
    /// Tests für <see cref="EpisodeFolderRenameService"/>. Echte Ordner in einem eigenen
    /// Temp-Verzeichnis, weil gerade das Umbenennen auf dem Dateisystem geprüft werden soll;
    /// die Tags kommen aus einem Fake.
    /// </summary>
    public sealed class EpisodeFolderRenameServiceTests : IDisposable
    {
        private const string Pattern = "{number:000} - {title}";

        private readonly string _series;
        private readonly Dictionary<string, (string, string)> _tags = [];

        /// <summary>Legt den Serienordner in einem eigenen Temp-Verzeichnis an.</summary>
        public EpisodeFolderRenameServiceTests()
        {
            string root = Directory.CreateTempSubdirectory("echoplay_rename_").FullName;
            _series = Directory.CreateDirectory(Path.Combine(root, "Die drei Ausrufezeichen")).FullName;
        }

        /// <summary>Löscht das Temp-Verzeichnis samt Inhalt.</summary>
        public void Dispose()
        {
            Directory.Delete(Path.GetDirectoryName(_series)!, recursive: true);
        }

        [Fact]
        public void Plan_TitleFromAlbumTag_BuildsNameFromPattern()
        {
            AddCompanion();
            string folder = AddEpisode("Die drei Ausrufezeichen - 087 - Liebestrank", album: "Folge 87: Geheimnisvoller Liebestrank");

            FolderRenameItem item = ItemFor(CreateService().Plan(_series, Pattern), folder);

            Assert.Equal(FolderRenameStatus.Rename, item.Status);
            Assert.Equal("087 - Geheimnisvoller Liebestrank", item.TargetName);
            Assert.True(item.TitleFromTag);
            Assert.Equal(folder, item.CurrentPath);
        }

        [Fact]
        public void Plan_TagDiffersOnlyInCase_KeepsFolderTitleAndLeavesNamedFolderAlone()
        {
            // Der Tag schreibt „die Handy-Falle"; der Ordner heißt schon richtig nach dem Muster.
            AddCompanion();
            string folder = AddEpisode("001 - Die Handy-Falle", album: "Folge 1: die Handy-Falle");

            FolderRenameItem item = ItemFor(CreateService().Plan(_series, Pattern), folder);

            Assert.Equal(FolderRenameStatus.Unchanged, item.Status);
            Assert.False(item.TitleFromTag);
        }

        [Fact]
        public void Plan_TagDiffersOnlyInCase_RenamesWithFolderSpelling()
        {
            // Der Name folgt nicht dem Muster und wird umbenannt, aber mit der Schreibweise des
            // Ordners statt der nachlässigeren des Tags.
            AddCompanion();
            string folder = AddEpisode("Die drei Ausrufezeichen - 002 - Betrug beim Casting", album: "Betrug Beim Casting");

            FolderRenameItem item = ItemFor(CreateService().Plan(_series, Pattern), folder);

            Assert.Equal("002 - Betrug beim Casting", item.TargetName);
            Assert.False(item.TitleFromTag);
        }

        [Fact]
        public void Plan_NoAlbumTag_TakesTitleFromFolderName()
        {
            AddCompanion();
            string folder = AddEpisode("Die drei Ausrufezeichen - 088 - Die Krimi-Verschwörung", album: null);

            FolderRenameItem item = ItemFor(CreateService().Plan(_series, Pattern), folder);

            Assert.Equal("088 - Die Krimi-Verschwörung", item.TargetName);
            Assert.False(item.TitleFromTag);
        }

        [Fact]
        public void Plan_SpecialFolderWithoutNumber_KeepsItsName()
        {
            _ = AddEpisode("087 - Geheimnisvoller Liebestrank", album: "Geheimnisvoller Liebestrank");
            _ = AddEpisode("Die drei Ausrufezeichen - XXX - Adventskalender 2013", album: "Adventskalender 2013");

            FolderRenamePlan plan = CreateService().Plan(_series, Pattern);

            FolderRenameItem special = Assert.Single(plan.Items, item => item.CurrentName.Contains("XXX", StringComparison.Ordinal));
            Assert.Equal(FolderRenameStatus.NoNumber, special.Status);
            Assert.Equal(special.CurrentName, special.TargetName);
        }

        [Fact]
        public void Plan_AlreadyNamedByPattern_IsUnchanged()
        {
            _ = AddEpisode("087 - Geheimnisvoller Liebestrank", album: "Folge 87: Geheimnisvoller Liebestrank");

            FolderRenamePlan plan = CreateService().Plan(_series, Pattern);

            Assert.Equal(FolderRenameStatus.Unchanged, Assert.Single(plan.Items).Status);
            Assert.Empty(plan.Renames);
        }

        [Fact]
        public void Plan_TargetTakenByAnotherFolder_IsConflict()
        {
            AddCompanion();
            string folder = AddEpisode("Serie - 087 - Alt", album: "Geheimnisvoller Liebestrank");
            _ = Directory.CreateDirectory(Path.Combine(_series, "087 - Geheimnisvoller Liebestrank"));

            FolderRenameItem item = ItemFor(CreateService().Plan(_series, Pattern), folder);

            Assert.Equal(FolderRenameStatus.Conflict, item.Status);
        }

        [Fact]
        public void Plan_TwoFoldersWithSameTarget_BothAreConflicts()
        {
            // „118.1" und „118.2" mit demselben Album-Tag ergäben denselben Namen.
            AddCompanion();
            string partOne = AddEpisode("118.1 - Das Tal der Angst - Teil 1", album: "Das Tal der Angst");
            string partTwo = AddEpisode("118.2 - Das Tal der Angst - Teil 2", album: "Das Tal der Angst");

            FolderRenamePlan plan = CreateService().Plan(_series, Pattern);

            Assert.Equal(FolderRenameStatus.Conflict, ItemFor(plan, partOne).Status);
            Assert.Equal(FolderRenameStatus.Conflict, ItemFor(plan, partTwo).Status);
        }

        [Fact]
        public void Plan_SeriesWithoutNumbers_RenamesNothing()
        {
            _ = AddEpisode("Staffel 1", album: "Staffel 1");
            _ = AddEpisode("Staffel 2", album: "Staffel 2");

            FolderRenamePlan plan = CreateService().Plan(_series, Pattern);

            Assert.False(plan.SeriesIsNumbered);
            Assert.Empty(plan.Renames);
        }

        [Fact]
        public void Plan_PatternWithWildcard_IsNotSupported()
        {
            _ = AddEpisode("087 - Alt", album: "Neu");

            FolderRenamePlan plan = CreateService().Plan(_series, "{*} - {number:000} - {title}");

            Assert.False(plan.PatternSupported);
            Assert.Empty(plan.Renames);
        }

        [Fact]
        public void Plan_FolderWithoutAudio_IsIgnored()
        {
            _ = Directory.CreateDirectory(Path.Combine(_series, "Cover"));
            _ = AddEpisode("087 - Alt", album: "Neu");

            _ = Assert.Single(CreateService().Plan(_series, Pattern).Items);
        }

        [Fact]
        public void Execute_RenamesFoldersAndRemovesJournal()
        {
            AddCompanion();
            string folder = AddEpisode("Serie - 087 - Alt", album: "Geheimnisvoller Liebestrank");
            EpisodeFolderRenameService service = CreateService();

            FolderRenameResult result = service.Execute(service.Plan(_series, Pattern));

            string target = Path.Combine(_series, "087 - Geheimnisvoller Liebestrank");
            Assert.Equal([(folder, target)], result.Moves);
            Assert.True(Directory.Exists(target));
            Assert.False(Directory.Exists(folder));
            Assert.False(File.Exists(Path.Combine(_series, EpisodeFolderRenameService.JournalFileName)));
        }

        [Fact]
        public void Execute_OnlyCaseDiffers_Renames()
        {
            // Das Muster schreibt „Folge" groß, der Ordner klein. Windows hält beide Schreibweisen
            // für denselben Ordner; ein direktes Umbenennen scheiterte oder bewirkte nichts.
            _ = AddEpisode("folge 087 - Geheimnisvoller Liebestrank", album: "Geheimnisvoller Liebestrank");
            _ = AddEpisode("folge 086 - Rätselhafter Raub", album: "Rätselhafter Raub");
            EpisodeFolderRenameService service = CreateService();

            _ = service.Execute(service.Plan(_series, "Folge {number:000} - {title}"));

            Assert.Contains("Folge 087 - Geheimnisvoller Liebestrank", Directory.GetDirectories(_series).Select(Path.GetFileName));
        }

        [Fact]
        public void Execute_TargetAppearedAfterPlan_SkipsFolder()
        {
            AddCompanion();
            string folder = AddEpisode("Serie - 087 - Alt", album: "Geheimnisvoller Liebestrank");
            EpisodeFolderRenameService service = CreateService();
            FolderRenamePlan plan = service.Plan(_series, Pattern);
            _ = Directory.CreateDirectory(Path.Combine(_series, "087 - Geheimnisvoller Liebestrank"));

            FolderRenameResult result = service.Execute(plan);

            Assert.Empty(result.Moves);
            Assert.Equal(1, result.SkippedCount);
            Assert.True(Directory.Exists(folder));
        }

        [Fact]
        public void Plan_PendingJournal_RestoresOldNamesFirst()
        {
            // Stand nach einem Abbruch: Ordner liegt schon unter dem neuen Namen, das Journal
            // nennt den alten.
            string oldPath = Path.Combine(_series, "Serie - 087 - Alt");
            string newPath = AddEpisode("087 - Geheimnisvoller Liebestrank", album: "Geheimnisvoller Liebestrank");
            File.WriteAllText(
                Path.Combine(_series, EpisodeFolderRenameService.JournalFileName),
                System.Text.Json.JsonSerializer.Serialize(new[] { new { Source = oldPath, Target = newPath } }));

            FolderRenamePlan plan = CreateService().Plan(_series, Pattern);

            Assert.Equal(1, plan.RecoveredCount);
            Assert.True(Directory.Exists(oldPath));
            Assert.False(File.Exists(Path.Combine(_series, EpisodeFolderRenameService.JournalFileName)));
        }

        /// <summary>
        /// Eine zweite, schon richtig benannte Folge. Eine einzelne Nummer aus den allgemeinen
        /// Mustern gilt sonst als Titelbestandteil, und die Serie als unnummeriert.
        /// </summary>
        private void AddCompanion() => _ = AddEpisode("086 - Rätselhafter Raub", album: "Rätselhafter Raub");

        private static FolderRenameItem ItemFor(FolderRenamePlan plan, string folder)
            => Assert.Single(plan.Items, item => item.CurrentPath == folder);

        private EpisodeFolderRenameService CreateService() => new(new FakeLoggerFactory(), new FakeTagTitleReader(_tags));

        private string AddEpisode(string folderName, string? album)
        {
            string folder = Directory.CreateDirectory(Path.Combine(_series, folderName)).FullName;
            string track = Path.Combine(folder, "01.mp3");
            File.WriteAllText(track, string.Empty);
            if (album is not null)
            {
                _tags[track] = (string.Empty, album);
            }

            return folder;
        }
    }
}
