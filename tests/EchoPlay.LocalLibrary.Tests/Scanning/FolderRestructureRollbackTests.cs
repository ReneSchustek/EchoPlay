using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.LocalLibrary.Tests.Fakes;
using EchoPlay.LocalLibrary.Tests.Infrastructure;

namespace EchoPlay.LocalLibrary.Tests.Scanning
{
    /// <summary>
    /// Prüft den Umbau einer Ordnerstruktur, wenn er mittendrin scheitert, und die
    /// Wiederherstellung nach einem Abbruch.
    /// </summary>
    /// <remarks>
    /// Der Umbau verschiebt die Dateien des Anwenders. Bricht er auf halbem Weg ab
    /// (abgezogenes Laufwerk, geschlossenes Fenster), liegt sein Bestand halb hier und
    /// halb dort. Deshalb wird jeder Schritt in ein Journal geschrieben und beim
    /// Scheitern zurückgenommen; bleibt das Journal liegen, holt der nächste Start es nach.
    ///
    /// Die Tests arbeiten auf echten Ordnern im Temp-Verzeichnis.
    /// </remarks>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public sealed class FolderRestructureRollbackTests : IDisposable
    {
        private readonly string _root;

        public FolderRestructureRollbackTests()
        {
            _root = Directory.CreateTempSubdirectory("echoplay_umbau_").FullName;
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        [Fact]
        public void Analyze_ForAFolderThatIsNotThere_ReturnsAnEmptyPreview()
        {
            FolderRestructureService sut = new(new FakeLoggerFactory());

            RestructurePreview preview = sut.Analyze(
                Path.Combine(_root, "gibt-es-nicht"), "{number} - {title}");

            Assert.True(preview.IsEmpty);
            Assert.Equal(0, preview.FolderCount);
        }

        [Fact]
        public void Analyze_ForAFolderThatCannotBeRead_ReturnsAnEmptyPreview()
        {
            using LockedFolder locked = LockedFolder.Create(_root);
            FolderRestructureService sut = new(new FakeLoggerFactory());

            // Der Ordner ist da, sein Inhalt nicht lesbar. Eine leere Vorschau ist die
            // ehrliche Antwort, der Dialog zeigt dann „nichts zu tun" statt zu stürzen.
            RestructurePreview preview = sut.Analyze(locked.Path, "{number} - {title}");

            Assert.True(preview.IsEmpty);
        }

        [Fact]
        public void Execute_WhenTheTargetFileAlreadyExists_SkipsIt()
        {
            string source = Path.Combine(_root, "001 - Der Fall.mp3");
            File.WriteAllText(source, "ton");

            string targetFolder = Directory.CreateDirectory(Path.Combine(_root, "001 - Der Fall")).FullName;
            string occupied = Path.Combine(targetFolder, "001 - Der Fall.mp3");
            File.WriteAllText(occupied, "anderer ton");

            FolderRestructureService sut = new(new FakeLoggerFactory());

            int verschoben = sut.Execute(new RestructurePreview
            {
                SeriesFolderPath = _root,
                FolderCount = 1,
                Actions =
                [
                    new RestructureAction
                    {
                        SourcePath = source,
                        TargetFolderPath = targetFolder,
                        TargetFolderName = "001 - Der Fall",
                        FileName = "001 - Der Fall.mp3",
                    },
                ],
            });

            // Überschreiben wäre Datenverlust. Die Datei bleibt liegen, wo sie ist.
            Assert.Equal(0, verschoben);
            Assert.Equal("anderer ton", File.ReadAllText(occupied));
            Assert.True(File.Exists(source));
        }

        /// <summary>Verschiebt die Datei unter ihrem Namen in den angegebenen Zielordner.</summary>
        private static RestructureAction Move(string source, string targetFolderPath, string targetFolderName)
            => new()
            {
                SourcePath = source,
                TargetFolderPath = targetFolderPath,
                TargetFolderName = targetFolderName,
                FileName = Path.GetFileName(source),
            };

        [Fact]
        public void Execute_WhenOneMoveFails_PutsEverythingBack()
        {
            string firstSource = Path.Combine(_root, "001 - Erste.mp3");
            string secondSource = Path.Combine(_root, "002 - Zweite.mp3");
            File.WriteAllText(firstSource, "ton eins");
            File.WriteAllText(secondSource, "ton zwei");

            FolderRestructureService sut = new(new FakeLoggerFactory());

            List<RestructureAction> actions =
            [
                Move(firstSource, Path.Combine(_root, "001 - Erste"), "001 - Erste"),

                // Ein Pfad mit Nullzeichen lässt sich nicht anlegen, hier bricht der Umbau ab.
                Move(secondSource, _root + "\\\0kaputt", "kaputt"),
            ];

            _ = Assert.ThrowsAny<Exception>(() => sut.Execute(new RestructurePreview
            {
                SeriesFolderPath = _root,
                FolderCount = 2,
                Actions = actions,
            }));

            // Alles oder nichts: Nach dem Rückweg liegen beide Dateien wieder da, wo sie
            // waren, und der angefangene Zielordner ist weg.
            Assert.True(File.Exists(firstSource));
            Assert.True(File.Exists(secondSource));
            Assert.False(Directory.Exists(Path.Combine(_root, "001 - Erste")));
        }

        [Fact]
        public void Recover_WithAJournalThatCannotBeRead_ReportsNothingRecovered()
        {
            File.WriteAllText(
                Path.Combine(_root, FolderRestructureService.JournalFileName),
                "das ist kein JSON");

            FolderRestructureService sut = new(new FakeLoggerFactory());

            int recovered = sut.TryRecoverPendingJournal(_root);

            // Ein unlesbares Journal ist ärgerlich, aber kein Grund, den Start abzubrechen.
            Assert.Equal(0, recovered);
        }

        [Fact]
        public void Recover_WithAnEmptyJournal_RemovesIt()
        {
            string journal = Path.Combine(_root, FolderRestructureService.JournalFileName);
            File.WriteAllText(journal, "[]");

            FolderRestructureService sut = new(new FakeLoggerFactory());

            int recovered = sut.TryRecoverPendingJournal(_root);

            Assert.Equal(0, recovered);
            Assert.False(File.Exists(journal));
        }

        [Fact]
        public void Recover_WithAnEntryWhoseFileIsGone_KeepsGoing()
        {
            string journal = Path.Combine(_root, FolderRestructureService.JournalFileName);
            File.WriteAllText(
                journal,
                """[{"Source":"C:\\gibt-es-nicht\\a.mp3","Destination":"C:\\gibt-es-auch-nicht\\a.mp3"}]""");

            FolderRestructureService sut = new(new FakeLoggerFactory());

            int recovered = sut.TryRecoverPendingJournal(_root);

            // Der Anwender kann die Datei zwischenzeitlich selbst zurückgeschoben haben.
            Assert.Equal(0, recovered);
        }
    }
}
