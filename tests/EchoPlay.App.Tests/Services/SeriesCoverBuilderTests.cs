using EchoPlay.App.Services;
using EchoPlay.App.Tests.Fakes;
using EchoPlay.Data.Entities.Library;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Prüft die Reihenfolge der Cover-Quellen für sich. Am Ansichtsmodell hing sie zuvor an
    /// der ganzen Seite und war nur über sie zu prüfen.
    /// </summary>
    /// <remarks>
    /// Das Bild selbst wird nicht geprüft: Ein <see cref="BitmapImage"/> ist im Testhost ohne
    /// WinUI nicht konstruierbar. Geprüft wird deshalb, <em>welche Quelle gefragt wird</em> —
    /// und genau das ist die Zusicherung dieses Typs.
    /// </remarks>
    public sealed class SeriesCoverBuilderTests
    {
        private static Series MakeSeries(string? localFolderPath) =>
            new() { Title = "TKKG", LocalFolderPath = localFolderPath };

        [Fact]
        public async Task BuildAsync_AsksTheDatabaseFirst()
        {
            FakeCoverService coverService = new();
            SeriesCoverBuilder builder = new(coverService);
            Series series = MakeSeries(localFolderPath: null);

            _ = await builder.BuildAsync(series);

            Guid gefragt = Assert.Single(coverService.SeriesCoverRequests);
            Assert.Equal(series.Id, gefragt);
        }

        [Fact]
        public async Task BuildAsync_WithoutCoverService_DoesNotFail()
        {
            // Ohne Dienst bleibt allein die Datei im Serienordner als Quelle.
            SeriesCoverBuilder builder = new(coverService: null);

            BitmapImage? cover = await builder.BuildAsync(MakeSeries(localFolderPath: null));

            Assert.Null(cover);
        }

        [Fact]
        public async Task BuildAsync_NoStoredCoverAndNoFolder_ReturnsNull()
        {
            SeriesCoverBuilder builder = new(new FakeCoverService());

            BitmapImage? cover = await builder.BuildAsync(MakeSeries(localFolderPath: null));

            Assert.Null(cover);
        }

        [Fact]
        public async Task BuildAsync_FolderWithoutCoverFile_ReturnsNull()
        {
            // Ein Ordner ohne cover.jpg ist kein Fehler – die Kachel bleibt ohne Bild.
            string ordner = Path.Combine(Path.GetTempPath(), "EchoPlayCoverTest_" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(ordner);

            try
            {
                SeriesCoverBuilder builder = new(new FakeCoverService());

                BitmapImage? cover = await builder.BuildAsync(MakeSeries(ordner));

                Assert.Null(cover);
            }
            finally
            {
                Directory.Delete(ordner, recursive: true);
            }
        }

        [Fact]
        public async Task BuildAsync_NullSeries_IsRejected()
        {
            SeriesCoverBuilder builder = new(new FakeCoverService());

            _ = await Assert.ThrowsAsync<ArgumentNullException>(() => builder.BuildAsync(null!));
        }

        [Fact]
        public async Task TryBuildAsync_ReadErrorIsSwallowed()
        {
            // Beim Nachladen im Hintergrund darf ein unlesbares Cover den Aufbau der Liste
            // nicht stören — dort bleibt der Platzhalter stehen.
            SeriesCoverBuilder builder = new(new ThrowingCoverService());

            BitmapImage? cover = await builder.TryBuildAsync(MakeSeries(localFolderPath: null));

            Assert.Null(cover);
        }

        [Fact]
        public async Task TryBuildAsync_OtherErrorsStillSurface()
        {
            // Nur Lesefehler werden geschluckt. Ein Programmierfehler soll sichtbar bleiben.
            SeriesCoverBuilder builder = new(new ThrowingCoverService(new InvalidOperationException("Fehler im Ablauf")));

            _ = await Assert.ThrowsAsync<InvalidOperationException>(
                () => builder.TryBuildAsync(MakeSeries(localFolderPath: null)));
        }

        /// <summary>Cover-Dienst, der beim Lesen scheitert.</summary>
        private sealed class ThrowingCoverService : ICoverService
        {
            private readonly Exception _fehler;

            public ThrowingCoverService(Exception? fehler = null)
            {
                _fehler = fehler ?? new IOException("Datei gesperrt");
            }

            public Task<BitmapImage?> GetSeriesCoverImageAsync(Guid seriesId, CancellationToken cancellationToken = default) =>
                Task.FromException<BitmapImage?>(_fehler);

            public Task<BitmapImage?> GetEpisodeCoverImageAsync(Guid episodeId, CancellationToken cancellationToken = default) =>
                Task.FromResult<BitmapImage?>(null);

            public Task<IReadOnlyDictionary<Guid, byte[]>> GetEpisodeCoverBytesAsync(IReadOnlyList<Guid> episodeIds, CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyDictionary<Guid, byte[]>>(new Dictionary<Guid, byte[]>());

            public Task SetSeriesCoverAsync(Guid seriesId, byte[] imageData, string? sourceUrl = null, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;

            public Task SetEpisodeCoverAsync(Guid episodeId, byte[] imageData, string? sourceUrl = null, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;

            public Task<bool> HasSeriesCoverAsync(Guid seriesId, CancellationToken cancellationToken = default) =>
                Task.FromResult(false);
        }
    }
}
