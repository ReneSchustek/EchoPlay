using EchoPlay.LocalLibrary.Tests.Fakes;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.LocalLibrary.Tests.Infrastructure;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.LocalLibrary.Tests.Cover
{
    /// <summary>
    /// Prüft die Cover-Beschaffung, wenn ein Ordner oder eine Datei nicht lesbar ist.
    /// </summary>
    /// <remarks>
    /// Cover liegen im Bestand des Anwenders: auf Netzlaufwerken, in Ordnern anderer
    /// Konten, in Dateien, die gerade geschrieben werden. Jede Stufe der Suche muss
    /// deshalb auf die nächste weiterreichen können, statt aufzugeben. Ein fehlendes
    /// Bild ist ein Schönheitsfehler; ein Abbruch kostet die ganze Ansicht.
    ///
    /// Die Tests arbeiten auf echten Ordnern im Temp-Verzeichnis und nehmen jede Sperre
    /// wieder zurück.
    /// </remarks>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public sealed class LocalCoverUnreadableTests : IDisposable
    {
        private readonly string _series;

        public LocalCoverUnreadableTests()
        {
            _series = Directory.CreateTempSubdirectory("echoplay_cover_").FullName;
        }

        public void Dispose()
        {
            if (Directory.Exists(_series))
            {
                Directory.Delete(_series, recursive: true);
            }
        }

        [Fact]
        public async Task Resolve_WithAnUnreadableCoverSubfolder_MovesOn()
        {
            using LockedFolder locked = LockedFolder.Create(_series, "Cover");

            CapturingLoggerFactory log = new();
            LocalCoverService sut = new(BuildCoverService(), log);

            byte[]? resolved = await sut.ResolveAsync(_series, null, TestContext.Current.CancellationToken);

            // Der Unterordner „Cover" ist die erste Stufe. Ist er nicht lesbar, geht es
            // mit den Dateien im Serienordner weiter, nicht mit einem Fehler, aber mit Spur.
            Assert.Null(resolved);
            Assert.Contains(log.Entries, entry => entry.Level == "Warning" && entry.Message.Contains("Cover-Unterordner", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Resolve_WithAnUnreadableSeriesFolder_ReturnsNothing()
        {
            using LockedFolder locked = LockedFolder.Create(_series, "Gesperrte Serie");

            CapturingLoggerFactory log = new();
            LocalCoverService sut = new(BuildCoverService(), log);

            byte[]? resolved = await sut.ResolveAsync(locked.Path, null, TestContext.Current.CancellationToken);

            Assert.Null(resolved);
            Assert.Contains(log.Entries, entry => entry.Level == "Warning");
        }

        [Fact]
        public async Task Resolve_WithACoverThatCannotBeRead_MovesOn()
        {
            string coverFolder = Directory.CreateDirectory(Path.Combine(_series, "Cover")).FullName;
            string vorne = Path.Combine(coverFolder, "front.jpg");
            await File.WriteAllBytesAsync(vorne, [0x01], TestContext.Current.CancellationToken);

            using FileStream sperre = new(vorne, FileMode.Open, FileAccess.Read, FileShare.None);

            LocalCoverService sut = new(BuildCoverService(), new FakeLoggerFactory());

            // Die Datei wird gerade geschrieben. Der Dienst probiert den nächsten
            // Kandidaten, statt mit leeren Händen abzubrechen.
            byte[]? resolved = await sut.ResolveAsync(_series, null, TestContext.Current.CancellationToken);

            Assert.Null(resolved);
        }

        [Fact]
        public async Task Load_WithACoverFileWithoutReadRights_FallsBackToNothing()
        {
            string coverPath = Path.Combine(_series, "cover.jpg");
            await File.WriteAllBytesAsync(coverPath, [0x01], TestContext.Current.CancellationToken);

            using FileStream sperre = new(coverPath, FileMode.Open, FileAccess.Read, FileShare.None);

            byte[]? geladen = await new LocalCoverLoader(new FakeLoggerFactory()).LoadAsync(_series, firstTrackPath: null);

            Assert.Null(geladen);
        }

        [Fact]
        public async Task Load_WithATrackThatIsLocked_ReturnsNothing()
        {
            string track = AudioTestFiles.CreateMp3(_series);

            using FileStream sperre = new(track, FileMode.Open, FileAccess.Read, FileShare.None);

            // Während die Wiedergabe läuft, hält sie die Datei. Der Cover-Lader darf
            // daran nicht scheitern, die Kachel bleibt eben grau.
            byte[]? geladen = await new LocalCoverLoader(new FakeLoggerFactory()).LoadAsync(_series, track);

            Assert.Null(geladen);
        }

        [Fact]
        public async Task Load_WithATrackInAnUnknownFormat_ReturnsNothing()
        {
            string fremd = Path.Combine(_series, "01 - Fremd.xyz");
            await File.WriteAllTextAsync(fremd, "irgendein Inhalt", TestContext.Current.CancellationToken);

            // Eine Endung, die die Kennzeichnungs-Bibliothek nicht kennt, ist kein Fehler
            // des Anwenders, sie liefert einfach kein Bild.
            byte[]? geladen = await new LocalCoverLoader(new FakeLoggerFactory()).LoadAsync(_series, fremd);

            Assert.Null(geladen);
        }

        private static CoverService BuildCoverService()
        {
            HttpClient http = new(new StubHandler());
            return new CoverService(http);
        }

        /// <summary>Antwortgeber, der nie ein Bild liefert.</summary>
        private sealed class StubHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
