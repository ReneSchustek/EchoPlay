using EchoPlay.LocalLibrary.Models;
using EchoPlay.LocalLibrary.Scanning;
using EchoPlay.LocalLibrary.Tests.Fakes;
using EchoPlay.LocalLibrary.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;

namespace EchoPlay.LocalLibrary.Tests.Scanning
{
    /// <summary>
    /// Prüft den Umbau einer Ordnerstruktur, wenn er mittendrin scheitert, und die
    /// Wiederherstellung nach einem Abbruch.
    /// </summary>
    /// <remarks>
    /// Der Umbau verschiebt die Dateien des Anwenders. Bricht er auf halbem Weg ab —
    /// abgezogenes Laufwerk, geschlossenes Fenster —, liegt sein Bestand halb hier und
    /// halb dort. Deshalb wird jeder Schritt in ein Journal geschrieben und beim
    /// Scheitern zurückgenommen; bleibt das Journal liegen, holt der nächste Start es nach.
    ///
    /// Die Tests arbeiten auf echten Ordnern im Temp-Verzeichnis.
    /// </remarks>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public sealed class FolderRestructureRollbackTests : IDisposable
    {
        private readonly string _wurzel;

        public FolderRestructureRollbackTests()
        {
            _wurzel = Directory.CreateTempSubdirectory("echoplay_umbau_").FullName;
        }

        public void Dispose()
        {
            if (Directory.Exists(_wurzel))
            {
                Directory.Delete(_wurzel, recursive: true);
            }
        }

        [Fact]
        public void Analyze_ForAFolderThatIsNotThere_ReturnsAnEmptyPreview()
        {
            FolderRestructureService sut = new(new FakeLoggerFactory());

            RestructurePreview vorschau = sut.Analyze(
                Path.Combine(_wurzel, "gibt-es-nicht"), "{number} - {title}");

            Assert.True(vorschau.IsEmpty);
            Assert.Equal(0, vorschau.FolderCount);
        }

        [Fact]
        public void Analyze_ForAFolderThatCannotBeRead_ReturnsAnEmptyPreview()
        {
            using LockedFolder gesperrt = LockedFolder.Create(_wurzel);
            FolderRestructureService sut = new(new FakeLoggerFactory());

            // Der Ordner ist da, sein Inhalt nicht lesbar. Eine leere Vorschau ist die
            // ehrliche Antwort — der Dialog zeigt dann „nichts zu tun" statt zu stürzen.
            RestructurePreview vorschau = sut.Analyze(gesperrt.Path, "{number} - {title}");

            Assert.True(vorschau.IsEmpty);
        }

        [Fact]
        public void Execute_WhenTheTargetFileAlreadyExists_SkipsIt()
        {
            string quelle = Path.Combine(_wurzel, "001 - Der Fall.mp3");
            File.WriteAllText(quelle, "ton");

            string zielOrdner = Directory.CreateDirectory(Path.Combine(_wurzel, "001 - Der Fall")).FullName;
            string belegt = Path.Combine(zielOrdner, "001 - Der Fall.mp3");
            File.WriteAllText(belegt, "anderer ton");

            FolderRestructureService sut = new(new FakeLoggerFactory());

            int verschoben = sut.Execute(new RestructurePreview
            {
                SeriesFolderPath = _wurzel,
                FolderCount = 1,
                Actions =
                [
                    new RestructureAction
                    {
                        SourcePath = quelle,
                        TargetFolderPath = zielOrdner,
                        TargetFolderName = "001 - Der Fall",
                        FileName = "001 - Der Fall.mp3",
                    },
                ],
            });

            // Überschreiben wäre Datenverlust. Die Datei bleibt liegen, wo sie ist.
            Assert.Equal(0, verschoben);
            Assert.Equal("anderer ton", File.ReadAllText(belegt));
            Assert.True(File.Exists(quelle));
        }

        [Fact]
        public void Execute_WhenOneMoveFails_PutsEverythingBack()
        {
            string ersteQuelle = Path.Combine(_wurzel, "001 - Erste.mp3");
            string zweiteQuelle = Path.Combine(_wurzel, "002 - Zweite.mp3");
            File.WriteAllText(ersteQuelle, "ton eins");
            File.WriteAllText(zweiteQuelle, "ton zwei");

            FolderRestructureService sut = new(new FakeLoggerFactory());

            List<RestructureAction> aktionen =
            [
                new RestructureAction
                {
                    SourcePath = ersteQuelle,
                    TargetFolderPath = Path.Combine(_wurzel, "001 - Erste"),
                    TargetFolderName = "001 - Erste",
                    FileName = "001 - Erste.mp3",
                },
                new RestructureAction
                {
                    // Ein Pfad mit Nullzeichen lässt sich nicht anlegen — hier bricht der Umbau ab.
                    SourcePath = zweiteQuelle,
                    TargetFolderPath = _wurzel + "\\\0kaputt",
                    TargetFolderName = "kaputt",
                    FileName = "002 - Zweite.mp3",
                },
            ];

            _ = Assert.ThrowsAny<Exception>(() => sut.Execute(new RestructurePreview
            {
                SeriesFolderPath = _wurzel,
                FolderCount = 2,
                Actions = aktionen,
            }));

            // Alles oder nichts: Nach dem Rückweg liegen beide Dateien wieder da, wo sie
            // waren, und der angefangene Zielordner ist weg.
            Assert.True(File.Exists(ersteQuelle));
            Assert.True(File.Exists(zweiteQuelle));
            Assert.False(Directory.Exists(Path.Combine(_wurzel, "001 - Erste")));
        }

        [Fact]
        public void Recover_WithAJournalThatCannotBeRead_ReportsNothingRecovered()
        {
            File.WriteAllText(
                Path.Combine(_wurzel, FolderRestructureService.JournalFileName),
                "das ist kein JSON");

            FolderRestructureService sut = new(new FakeLoggerFactory());

            int recovered = sut.TryRecoverPendingJournal(_wurzel);

            // Ein unlesbares Journal ist ärgerlich, aber kein Grund, den Start abzubrechen.
            Assert.Equal(0, recovered);
        }

        [Fact]
        public void Recover_WithAnEmptyJournal_RemovesIt()
        {
            string journal = Path.Combine(_wurzel, FolderRestructureService.JournalFileName);
            File.WriteAllText(journal, "[]");

            FolderRestructureService sut = new(new FakeLoggerFactory());

            int recovered = sut.TryRecoverPendingJournal(_wurzel);

            Assert.Equal(0, recovered);
            Assert.False(File.Exists(journal));
        }

        [Fact]
        public void Recover_WithAnEntryWhoseFileIsGone_KeepsGoing()
        {
            string journal = Path.Combine(_wurzel, FolderRestructureService.JournalFileName);
            File.WriteAllText(
                journal,
                """[{"Source":"C:\\gibt-es-nicht\\a.mp3","Destination":"C:\\gibt-es-auch-nicht\\a.mp3"}]""");

            FolderRestructureService sut = new(new FakeLoggerFactory());

            int recovered = sut.TryRecoverPendingJournal(_wurzel);

            // Der Anwender kann die Datei zwischenzeitlich selbst zurückgeschoben haben.
            Assert.Equal(0, recovered);
        }
    }
}
