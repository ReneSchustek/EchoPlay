using EchoPlay.LocalLibrary.Tests.Fakes;
using EchoPlay.LocalLibrary.Cover;
using EchoPlay.LocalLibrary.Tests.Infrastructure;
using System.Net;

namespace EchoPlay.LocalLibrary.Tests.Cover
{
    /// <summary>
    /// Prüft die beiden letzten Stufen der Cover-Beschaffung: das Bild aus der
    /// Kennzeichnung der ersten Spur und den Download vom Anbieter.
    /// </summary>
    /// <remarks>
    /// Wer seine Hörspiele selbst gerippt hat, hat oft keine <c>cover.jpg</c> im Ordner,
    /// aber fast immer ein Bild in der Kennzeichnung der MP3. Ohne diese Stufe blieben
    /// genau diese Serien in der Mediathek grau.
    ///
    /// Die Tests arbeiten auf echten Dateien im Temp-Verzeichnis. Der Download läuft über
    /// einen eingesetzten Antwortgeber; kein Test geht ins Netz.
    /// </remarks>
    public sealed class CoverFromTrackAndDownloadTests : IDisposable
    {
        private static readonly byte[] Image = [0x11, 0x22, 0x33, 0x44];

        private readonly string _folder;

        public CoverFromTrackAndDownloadTests()
        {
            _folder = Path.Combine(Path.GetTempPath(), $"echoplay-cover-{Guid.NewGuid():N}");
            _ = Directory.CreateDirectory(_folder);
        }

        public void Dispose()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }

        [Fact]
        public async Task Load_WithoutACoverFileButATaggedTrack_TakesTheEmbeddedImage()
        {
            string track = AudioTestFiles.CreateMp3WithCover(_folder, Image);

            byte[]? loaded = await new LocalCoverLoader(new FakeLoggerFactory()).LoadAsync(_folder, track);

            Assert.Equal(Image, loaded);
        }

        [Fact]
        public async Task Load_WithATrackWithoutAnImage_ReturnsNothing()
        {
            string track = AudioTestFiles.CreateMp3(_folder);

            byte[]? loaded = await new LocalCoverLoader(new FakeLoggerFactory()).LoadAsync(_folder, track);

            Assert.Null(loaded);
        }

        [Fact]
        public async Task Load_WithAFileThatIsNoAudio_ReturnsNothing()
        {
            string kaputt = Path.Combine(_folder, "01 - Kaputt.mp3");
            await File.WriteAllTextAsync(kaputt, "das ist kein Ton", TestContext.Current.CancellationToken);

            // Ein abgebrochener Download sieht genau so aus. Er darf das Einlesen des
            // Ordners nicht beenden.
            byte[]? loaded = await new LocalCoverLoader(new FakeLoggerFactory()).LoadAsync(_folder, kaputt);

            Assert.Null(loaded);
        }

        [Fact]
        public async Task Load_WithTheCoverFileLocked_FallsBackToTheTrack()
        {
            string track = AudioTestFiles.CreateMp3WithCover(_folder, Image);
            string coverPath = Path.Combine(_folder, "cover.jpg");
            await File.WriteAllBytesAsync(coverPath, [0x99], TestContext.Current.CancellationToken);

            using FileStream locked = new(coverPath, FileMode.Open, FileAccess.Read, FileShare.None);

            // Die Datei wird gerade geschrieben. Statt aufzugeben nimmt der Lader das
            // Bild aus der Kennzeichnung.
            byte[]? loaded = await new LocalCoverLoader(new FakeLoggerFactory()).LoadAsync(_folder, track);

            Assert.Equal(Image, loaded);
        }

        [Fact]
        public async Task Resolve_WithoutAnyLocalFile_DownloadsAndKeepsTheCover()
        {
            LocalCoverService sut = new(BuildCoverService(HttpStatusCode.OK, Image), new FakeLoggerFactory());

            byte[]? resolved = await sut.ResolveAsync(
                _folder, new Uri("https://i.example.invalid/tkkg.jpg"), TestContext.Current.CancellationToken);

            // Einmal geholt, bleibt das Bild im Ordner. Beim nächsten Start ohne Netz
            // ist es trotzdem da.
            Assert.Equal(Image, resolved);
            Assert.True(File.Exists(Path.Combine(_folder, "cover.jpg")));
        }

        [Fact]
        public async Task Resolve_WhenTheDownloadFails_ReturnsNothing()
        {
            LocalCoverService sut = new(BuildCoverService(HttpStatusCode.NotFound, []), new FakeLoggerFactory());

            byte[]? resolved = await sut.ResolveAsync(
                _folder, new Uri("https://i.example.invalid/tkkg.jpg"), TestContext.Current.CancellationToken);

            // Kein Bild ist kein Abbruchgrund. Die Serie erscheint mit Platzhalter.
            Assert.Null(resolved);
        }

        [Fact]
        public async Task Resolve_WhenTheFolderIsGone_ReturnsNothing()
        {
            string route = Path.Combine(Path.GetTempPath(), $"echoplay-weg-{Guid.NewGuid():N}");
            LocalCoverService sut = new(BuildCoverService(HttpStatusCode.OK, Image), new FakeLoggerFactory());

            byte[]? resolved = await sut.ResolveAsync(
                route, new Uri("https://i.example.invalid/tkkg.jpg"), TestContext.Current.CancellationToken);

            Assert.Null(resolved);
        }

        [Fact]
        public async Task SaveToDirectory_WithAnExistingCover_KeepsTheOldOne()
        {
            string target = Path.Combine(_folder, "cover.jpg");
            await File.WriteAllBytesAsync(target, [0x99], TestContext.Current.CancellationToken);

            await CoverService.SaveToDirectoryAsync(_folder, Image);

            // Der Anwender kann das Bild von Hand ausgetauscht haben. Es zu überschreiben
            // wäre die eine Änderung, die er nicht rückgängig machen kann.
            Assert.Equal([0x99], await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Download_DeliversTheBytesOfTheAnswer()
        {
            CoverService sut = BuildCoverService(HttpStatusCode.OK, Image);

            byte[] loaded = await sut.DownloadAsync(new Uri("https://i.example.invalid/tkkg.jpg"), TestContext.Current.CancellationToken);

            Assert.Equal(Image, loaded);
        }

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
