using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.LocalLibrary.Tests.Fakes;

namespace EchoPlay.LocalLibrary.Tests.Scanning
{
    /// <summary>
    /// Tests für den Ordnerstruktur-Assistenten.
    /// Nutzt temporäre Verzeichnisse mit echten Dateien – kein Mocking.
    /// </summary>
    public sealed class FolderRestructureServiceTests : IDisposable
    {
        private readonly string _root;
        private readonly FolderRestructureService _service;

        /// <summary>Temporäres Verzeichnis und Service für jeden Test erstellen.</summary>
        public FolderRestructureServiceTests()
        {
            _root = Directory.CreateTempSubdirectory("echoplay_restructure_").FullName;
            _service = new FolderRestructureService(new FakeLoggerFactory());
        }

        /// <summary>Temporäres Verzeichnis aufräumen.</summary>
        public void Dispose()
        {
            Directory.Delete(_root, recursive: true);
        }

        [Fact]
        public void Analyze_StandardPattern_CreatesCorrectActions()
        {
            // "Karl May - 001 - Durch die Wüste.mp3" → "001 - Durch die Wüste/"
            File.WriteAllText(Path.Combine(_root, "Karl May - 001 - Durch die Wüste.mp3"), string.Empty);
            File.WriteAllText(Path.Combine(_root, "Karl May - 002 - Durchs wilde Kurdistan.mp3"), string.Empty);

            RestructurePreview preview = _service.Analyze(_root, "{number:000} - {title}");

            Assert.Equal(2, preview.FileCount);
            Assert.Equal(2, preview.FolderCount);
            Assert.Contains(preview.Actions, a => a.TargetFolderName == "001 - Durch die Wüste");
            Assert.Contains(preview.Actions, a => a.TargetFolderName == "002 - Durchs wilde Kurdistan");
        }

        [Fact]
        public void Analyze_CassetteRip_SeparateFoldersPerSide()
        {
            // Pumuckl: 01a/01b → eigene Ordner (001, 002)
            File.WriteAllText(Path.Combine(_root, "01a Spuk in der Werkstatt.mp3"), string.Empty);
            File.WriteAllText(Path.Combine(_root, "01b Das verkaufte Bett.mp3"), string.Empty);

            RestructurePreview preview = _service.Analyze(_root, "{number:000} - {title}");

            Assert.Equal(2, preview.FileCount);
            Assert.Contains(preview.Actions, a => a.EpisodeNumber == 1 && a.TargetFolderName == "001 - Spuk in der Werkstatt");
            Assert.Contains(preview.Actions, a => a.EpisodeNumber == 2 && a.TargetFolderName == "002 - Das verkaufte Bett");
        }

        [Fact]
        public void Analyze_IgnoresNonAudioFiles()
        {
            File.WriteAllText(Path.Combine(_root, "Karl May - 001 - Titel.mp3"), string.Empty);
            File.WriteAllText(Path.Combine(_root, "cover.jpg"), string.Empty);
            File.WriteAllText(Path.Combine(_root, "info.nfo"), string.Empty);

            RestructurePreview preview = _service.Analyze(_root, "{number:000} - {title}");

            _ = Assert.Single(preview.Actions);
        }

        [Fact]
        public void Analyze_EmptyFolder_ReturnsEmpty()
        {
            RestructurePreview preview = _service.Analyze(_root, "{number:000} - {title}");

            Assert.True(preview.IsEmpty);
        }

        [Fact]
        public void Execute_MovesFiles_IntoSubfolders()
        {
            File.WriteAllText(Path.Combine(_root, "Karl May - 001 - Titel.mp3"), "audio-content");

            RestructurePreview preview = _service.Analyze(_root, "{number:000} - {title}");
            int moved = _service.Execute(preview);

            Assert.Equal(1, moved);

            // Datei wurde verschoben
            Assert.False(File.Exists(Path.Combine(_root, "Karl May - 001 - Titel.mp3")));
            Assert.True(File.Exists(Path.Combine(_root, "001 - Titel", "Karl May - 001 - Titel.mp3")));
        }

        [Fact]
        public void Execute_LeavesExistingSubfoldersAlone()
        {
            // Mischform: bestehender Unterordner + lose Dateien
            _ = Directory.CreateDirectory(Path.Combine(_root, "Kinderparty"));
            File.WriteAllText(Path.Combine(_root, "Kinderparty", "track.mp3"), string.Empty);
            File.WriteAllText(Path.Combine(_root, "01 Titel.mp3"), "audio");

            RestructurePreview preview = _service.Analyze(_root, "{number:000} - {title}");
            _ = _service.Execute(preview);

            // Der bestehende Unterordner bleibt unangetastet
            Assert.True(Directory.Exists(Path.Combine(_root, "Kinderparty")));
            Assert.True(File.Exists(Path.Combine(_root, "Kinderparty", "track.mp3")));
        }

        [Fact]
        public void Analyze_SortsByEpisodeNumber()
        {
            File.WriteAllText(Path.Combine(_root, "Karl May - 003 - Drei.mp3"), string.Empty);
            File.WriteAllText(Path.Combine(_root, "Karl May - 001 - Eins.mp3"), string.Empty);
            File.WriteAllText(Path.Combine(_root, "Karl May - 002 - Zwei.mp3"), string.Empty);

            RestructurePreview preview = _service.Analyze(_root, "{number:000} - {title}");

            Assert.Equal(1, preview.Actions[0].EpisodeNumber);
            Assert.Equal(2, preview.Actions[1].EpisodeNumber);
            Assert.Equal(3, preview.Actions[2].EpisodeNumber);
        }

        [Fact]
        public void Execute_RemovesJournal_AfterSuccessfulRun()
        {
            File.WriteAllText(Path.Combine(_root, "Karl May - 001 - Eins.mp3"), string.Empty);
            RestructurePreview preview = _service.Analyze(_root, "{number:000} - {title}");

            _ = _service.Execute(preview);

            Assert.False(File.Exists(Path.Combine(_root, FolderRestructureService.JournalFileName)));
        }

        [Fact]
        public void TryRecoverPendingJournal_RollsBackMovedFiles_FromStaleJournal()
        {
            // Simuliert einen gecrashten Umbau: Datei wurde verschoben, Journal liegt noch.
            string sourceFile = Path.Combine(_root, "Karl May - 001 - Eins.mp3");
            File.WriteAllText(sourceFile, "audio");

            RestructurePreview preview = _service.Analyze(_root, "{number:000} - {title}");

            // Zielordner anlegen, Datei manuell verschieben, Journal liegenlassen
            RestructureAction action = preview.Actions[0];
            _ = Directory.CreateDirectory(action.TargetFolderPath);
            string destination = Path.Combine(action.TargetFolderPath, action.FileName);
            File.Move(action.SourcePath, destination);

            string journalPath = Path.Combine(_root, FolderRestructureService.JournalFileName);
            File.WriteAllText(
                journalPath,
                $"[{{\"Source\":{System.Text.Json.JsonSerializer.Serialize(action.SourcePath)},\"Destination\":{System.Text.Json.JsonSerializer.Serialize(destination)}}}]");

            int recovered = _service.TryRecoverPendingJournal(_root);

            Assert.Equal(1, recovered);
            Assert.True(File.Exists(sourceFile));
            Assert.False(File.Exists(destination));
            Assert.False(File.Exists(journalPath));
        }

        [Fact]
        public void TryRecoverPendingJournal_ReturnsZero_WhenNoJournal()
        {
            int recovered = _service.TryRecoverPendingJournal(_root);
            Assert.Equal(0, recovered);
        }
    }
}
