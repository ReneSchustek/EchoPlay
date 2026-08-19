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
    /// Cover liegen im Bestand des Anwenders — auf Netzlaufwerken, in Ordnern anderer
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
        private readonly string _serie;

        public LocalCoverUnreadableTests()
        {
            _serie = Directory.CreateTempSubdirectory("echoplay_cover_").FullName;
        }

        public void Dispose()
        {
            if (Directory.Exists(_serie))
            {
                Directory.Delete(_serie, recursive: true);
            }
        }

        [Fact]
        public async Task Resolve_WithAnUnreadableCoverSubfolder_MovesOn()
        {
            using LockedFolder gesperrt = LockedFolder.Create(_serie, "Cover");

            LocalCoverService sut = new(BuildCoverService());

            byte[]? resolved = await sut.ResolveAsync(_serie, null, TestContext.Current.CancellationToken);

            // Der Unterordner „Cover" ist die erste Stufe. Ist er nicht lesbar, geht es
            // mit den Dateien im Serienordner weiter — nicht mit einem Fehler.
            Assert.Null(resolved);
        }

        [Fact]
        public async Task Resolve_WithAnUnreadableSeriesFolder_ReturnsNothing()
        {
            using LockedFolder gesperrt = LockedFolder.Create(_serie, "Gesperrte Serie");

            LocalCoverService sut = new(BuildCoverService());

            byte[]? resolved = await sut.ResolveAsync(gesperrt.Path, null, TestContext.Current.CancellationToken);

            Assert.Null(resolved);
        }

        [Fact]
        public async Task Resolve_WithACoverThatCannotBeRead_MovesOn()
        {
            string coverOrdner = Directory.CreateDirectory(Path.Combine(_serie, "Cover")).FullName;
            string vorne = Path.Combine(coverOrdner, "front.jpg");
            await File.WriteAllBytesAsync(vorne, [0x01], TestContext.Current.CancellationToken);

            using FileStream sperre = new(vorne, FileMode.Open, FileAccess.Read, FileShare.None);

            LocalCoverService sut = new(BuildCoverService());

            // Die Datei wird gerade geschrieben. Der Dienst probiert den nächsten
            // Kandidaten, statt mit leeren Händen abzubrechen.
            byte[]? resolved = await sut.ResolveAsync(_serie, null, TestContext.Current.CancellationToken);

            Assert.Null(resolved);
        }

        [Fact]
        public async Task Load_WithACoverFileWithoutReadRights_FallsBackToNothing()
        {
            string coverPfad = Path.Combine(_serie, "cover.jpg");
            await File.WriteAllBytesAsync(coverPfad, [0x01], TestContext.Current.CancellationToken);

            using FileStream sperre = new(coverPfad, FileMode.Open, FileAccess.Read, FileShare.None);

            byte[]? geladen = await new LocalCoverLoader().LoadAsync(_serie, firstTrackPath: null);

            Assert.Null(geladen);
        }

        [Fact]
        public async Task Load_WithATrackThatIsLocked_ReturnsNothing()
        {
            string spur = AudioTestFiles.CreateMp3(_serie);

            using FileStream sperre = new(spur, FileMode.Open, FileAccess.Read, FileShare.None);

            // Während die Wiedergabe läuft, hält sie die Datei. Der Cover-Lader darf
            // daran nicht scheitern — die Kachel bleibt eben grau.
            byte[]? geladen = await new LocalCoverLoader().LoadAsync(_serie, spur);

            Assert.Null(geladen);
        }

        [Fact]
        public async Task Load_WithATrackInAnUnknownFormat_ReturnsNothing()
        {
            string fremd = Path.Combine(_serie, "01 - Fremd.xyz");
            await File.WriteAllTextAsync(fremd, "irgendein Inhalt", TestContext.Current.CancellationToken);

            // Eine Endung, die die Kennzeichnungs-Bibliothek nicht kennt, ist kein Fehler
            // des Anwenders — sie liefert einfach kein Bild.
            byte[]? geladen = await new LocalCoverLoader().LoadAsync(_serie, fremd);

            Assert.Null(geladen);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

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
