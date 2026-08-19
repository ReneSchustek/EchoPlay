using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Entities.Library;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft den Weg vom Serienordner in die Ablage: Eine dort liegende <c>cover.jpg</c>
    /// wird beim ersten Aufbau übernommen, damit die Sammelabfrage der Kacheln sie
    /// danach findet.
    /// </summary>
    /// <remarks>
    /// Ohne diese Übernahme läse die Anwendung bei jedem Öffnen der Mediathek erneut von
    /// der Platte — bei achtzig Serien achtzigmal.
    ///
    /// Das fertige Bildobjekt entsteht am Fenster und bleibt im Testlauf leer; geprüft ist
    /// deshalb, was in der Ablage ankommt. Die Tests legen echte Dateien im
    /// Temp-Verzeichnis an und räumen sie wieder ab.
    /// </remarks>
    public sealed class SeriesCoverFolderTests
    {
        private static readonly byte[] CoverBytes = [0x21, 0x22, 0x23];

        [Fact]
        public async Task BuildAsync_WithCoverFileInTheFolder_PutsItIntoTheStore()
        {
            string folder = CreateFolder();
            try
            {
                await WriteCoverAsync(folder, CoverBytes);
                FakeCoverService coverService = new();
                SeriesCoverBuilder sut = new(coverService);

                _ = await sut.BuildAsync(new Series { Title = "TKKG", LocalFolderPath = folder });

                Assert.Contains(Guid.Empty, coverService.StoredSeriesCovers);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task BuildAsync_WithoutCoverFile_StoresNothing()
        {
            string folder = CreateFolder();
            try
            {
                FakeCoverService coverService = new();
                SeriesCoverBuilder sut = new(coverService);

                Assert.Null(await sut.BuildAsync(new Series { Title = "TKKG", LocalFolderPath = folder }));
                Assert.Empty(coverService.StoredSeriesCovers);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task BuildAsync_WithAnEmptyCoverFile_StoresNothing()
        {
            string folder = CreateFolder();
            try
            {
                await WriteCoverAsync(folder, []);
                FakeCoverService coverService = new();
                SeriesCoverBuilder sut = new(coverService);

                _ = await sut.BuildAsync(new Series { Title = "TKKG", LocalFolderPath = folder });

                // Eine leere Datei in die Ablage zu schreiben wäre schlimmer als keine:
                // Der spätere Lauf hielte die Serie für versorgt.
                Assert.Empty(coverService.StoredSeriesCovers);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public async Task BuildAsync_WithoutLocalFolder_ReturnsNothing()
        {
            SeriesCoverBuilder sut = new(new FakeCoverService());

            Assert.Null(await sut.BuildAsync(new Series { Title = "Nur online" }));
        }

        [Fact]
        public async Task BuildAsync_WithoutSeries_ThrowsArgumentNullException()
        {
            SeriesCoverBuilder sut = new(new FakeCoverService());

            _ = await Assert.ThrowsAsync<ArgumentNullException>(() => sut.BuildAsync(null!));
        }

        [Fact]
        public async Task BuildAsync_WithoutCoverService_StillReadsTheFolder()
        {
            string folder = CreateFolder();
            try
            {
                await WriteCoverAsync(folder, CoverBytes);
                SeriesCoverBuilder sut = new(coverService: null);

                // Im abgespeckten Betrieb ohne Ablage bleibt allein die Datei als Quelle;
                // der Aufbau darf daran nicht scheitern.
                _ = await sut.BuildAsync(new Series { Title = "TKKG", LocalFolderPath = folder });
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        private static async Task WriteCoverAsync(string folder, byte[] bytes)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(folder, EchoPlay.Core.CoverConstants.CoverFileName),
                bytes,
                TestContext.Current.CancellationToken);
        }

        private static string CreateFolder()
        {
            string path = Path.Combine(
                Path.GetTempPath(), $"echoplay-seriescover-{Path.GetRandomFileName()}");
            _ = Directory.CreateDirectory(path);
            return path;
        }
    }
}
