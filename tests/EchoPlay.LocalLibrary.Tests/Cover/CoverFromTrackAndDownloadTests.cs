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
    /// Prüft die beiden letzten Stufen der Cover-Beschaffung: das Bild aus der
    /// Kennzeichnung der ersten Spur und den Download vom Anbieter.
    /// </summary>
    /// <remarks>
    /// Wer seine Hörspiele selbst gerippt hat, hat oft keine <c>cover.jpg</c> im Ordner —
    /// aber fast immer ein Bild in der Kennzeichnung der MP3. Ohne diese Stufe blieben
    /// genau diese Serien in der Mediathek grau.
    ///
    /// Die Tests arbeiten auf echten Dateien im Temp-Verzeichnis. Der Download läuft über
    /// einen eingesetzten Antwortgeber; kein Test geht ins Netz.
    /// </remarks>
    public sealed class CoverFromTrackAndDownloadTests : IDisposable
    {
        private static readonly byte[] Bild = [0x11, 0x22, 0x33, 0x44];

        private readonly string _ordner;

        public CoverFromTrackAndDownloadTests()
        {
            _ordner = Path.Combine(Path.GetTempPath(), $"echoplay-cover-{Guid.NewGuid():N}");
            _ = Directory.CreateDirectory(_ordner);
        }

        public void Dispose()
        {
            if (Directory.Exists(_ordner))
            {
                Directory.Delete(_ordner, recursive: true);
            }
        }

        [Fact]
        public async Task Load_WithoutACoverFileButATaggedTrack_TakesTheEmbeddedImage()
        {
            string spur = AudioTestFiles.CreateMp3WithCover(_ordner, Bild);

            byte[]? geladen = await new LocalCoverLoader().LoadAsync(_ordner, spur);

            Assert.Equal(Bild, geladen);
        }

        [Fact]
        public async Task Load_WithATrackWithoutAnImage_ReturnsNothing()
        {
            string spur = AudioTestFiles.CreateMp3(_ordner);

            byte[]? geladen = await new LocalCoverLoader().LoadAsync(_ordner, spur);

            Assert.Null(geladen);
        }

        [Fact]
        public async Task Load_WithAFileThatIsNoAudio_ReturnsNothing()
        {
            string kaputt = Path.Combine(_ordner, "01 - Kaputt.mp3");
            await File.WriteAllTextAsync(kaputt, "das ist kein Ton", TestContext.Current.CancellationToken);

            // Ein abgebrochener Download sieht genau so aus. Er darf das Einlesen des
            // Ordners nicht beenden.
            byte[]? geladen = await new LocalCoverLoader().LoadAsync(_ordner, kaputt);

            Assert.Null(geladen);
        }

        [Fact]
        public async Task Load_WithTheCoverFileLocked_FallsBackToTheTrack()
        {
            string spur = AudioTestFiles.CreateMp3WithCover(_ordner, Bild);
            string coverPfad = Path.Combine(_ordner, "cover.jpg");
            await File.WriteAllBytesAsync(coverPfad, [0x99], TestContext.Current.CancellationToken);

            using FileStream gesperrt = new(coverPfad, FileMode.Open, FileAccess.Read, FileShare.None);

            // Die Datei wird gerade geschrieben. Statt aufzugeben nimmt der Lader das
            // Bild aus der Kennzeichnung.
            byte[]? geladen = await new LocalCoverLoader().LoadAsync(_ordner, spur);

            Assert.Equal(Bild, geladen);
        }

        [Fact]
        public async Task Resolve_WithoutAnyLocalFile_DownloadsAndKeepsTheCover()
        {
            LocalCoverService sut = new(BuildCoverService(HttpStatusCode.OK, Bild));

            byte[]? resolved = await sut.ResolveAsync(
                _ordner, "https://i.example.invalid/tkkg.jpg", TestContext.Current.CancellationToken);

            // Einmal geholt, bleibt das Bild im Ordner — beim nächsten Start ohne Netz
            // ist es trotzdem da.
            Assert.Equal(Bild, resolved);
            Assert.True(File.Exists(Path.Combine(_ordner, "cover.jpg")));
        }

        [Fact]
        public async Task Resolve_WhenTheDownloadFails_ReturnsNothing()
        {
            LocalCoverService sut = new(BuildCoverService(HttpStatusCode.NotFound, []));

            byte[]? resolved = await sut.ResolveAsync(
                _ordner, "https://i.example.invalid/tkkg.jpg", TestContext.Current.CancellationToken);

            // Kein Bild ist kein Abbruchgrund. Die Serie erscheint mit Platzhalter.
            Assert.Null(resolved);
        }

        [Fact]
        public async Task Resolve_WhenTheFolderIsGone_ReturnsNothing()
        {
            string weg = Path.Combine(Path.GetTempPath(), $"echoplay-weg-{Guid.NewGuid():N}");
            LocalCoverService sut = new(BuildCoverService(HttpStatusCode.OK, Bild));

            byte[]? resolved = await sut.ResolveAsync(
                weg, "https://i.example.invalid/tkkg.jpg", TestContext.Current.CancellationToken);

            Assert.Null(resolved);
        }

        [Fact]
        public async Task SaveToDirectory_WithAnExistingCover_KeepsTheOldOne()
        {
            string ziel = Path.Combine(_ordner, "cover.jpg");
            await File.WriteAllBytesAsync(ziel, [0x99], TestContext.Current.CancellationToken);

            await CoverService.SaveToDirectoryAsync(_ordner, Bild);

            // Der Anwender kann das Bild von Hand ausgetauscht haben. Es zu überschreiben
            // wäre die eine Änderung, die er nicht rückgängig machen kann.
            Assert.Equal([0x99], await File.ReadAllBytesAsync(ziel, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Download_DeliversTheBytesOfTheAnswer()
        {
            CoverService sut = BuildCoverService(HttpStatusCode.OK, Bild);

            byte[] geladen = await sut.DownloadAsync("https://i.example.invalid/tkkg.jpg");

            Assert.Equal(Bild, geladen);
        }

        // ── Aufbau ───────────────────────────────────────────────────────────────

        private static CoverService BuildCoverService(HttpStatusCode status, byte[] inhalt)
        {
            HttpClient http = new(new StubHandler(status, inhalt));
            return new CoverService(http);
        }

        /// <summary>Antwortgeber mit festem Zustand und festen Bytes.</summary>
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly byte[] _inhalt;

            public StubHandler(HttpStatusCode status, byte[] inhalt)
            {
                _status = status;
                _inhalt = inhalt;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();

                return Task.FromResult(new HttpResponseMessage(_status)
                {
                    Content = new ByteArrayContent(_inhalt),
                });
            }
        }
    }
}
