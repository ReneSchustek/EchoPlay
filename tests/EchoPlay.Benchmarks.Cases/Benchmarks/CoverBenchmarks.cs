using System.Runtime.InteropServices.WindowsRuntime;
using BenchmarkDotNet.Attributes;
using EchoPlay.Benchmarks.Cases.Infrastructure;
using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace EchoPlay.Benchmarks.Cases.Benchmarks
{
    /// <summary>
    /// Was ein Cover kostet, bis es auf einer Kachel steht: der Stapelzugriff auf die Bilddaten
    /// einer Folgenliste und das Dekodieren, einmal in voller Größe und einmal in Kachelgröße.
    /// </summary>
    /// <remarks>
    /// Die Anwendung dekodiert über <c>BitmapImage</c> mit <c>DecodePixelWidth</c>. Das braucht eine
    /// laufende Oberfläche und lässt sich hier nicht starten. Gemessen wird deshalb derselbe Weg
    /// über <see cref="BitmapDecoder"/>, den Windows darunter nimmt: Bilddaten in einen Strom, Größe
    /// beim Dekodieren festlegen, Pixel abholen. Die Zahlen gelten für diesen Weg; was die
    /// Oberfläche danach noch tut, Hochladen auf die Grafikkarte und Zeichnen, fehlt in ihnen.
    /// </remarks>
    [MemoryDiagnoser]
    public class CoverBenchmarks
    {
        /// <summary>Breite, in der die Kacheln ihre Cover dekodieren.</summary>
        public const int TileDecodeWidth = 240;

        private readonly SilentLoggerFactory _loggers = new();
        private string _databasePath = string.Empty;
        private IReadOnlyList<Guid> _episodesWithCover = [];
        private byte[] _cover = [];
        private EchoPlayDbContext? _context;

        /// <summary>Fängt die Befehle einer Messung für den Bericht der Abfragepläne mit.</summary>
        internal SqlCommandCapture? Capture { get; init; }

        /// <summary>Mittlere Größe der erzeugten Motive in Byte, für den Bericht.</summary>
        internal double MeanCoverBytes { get; private set; }

        /// <summary>Legt die Messdatenbank mit Bestand und Covern einmal je Messlauf an.</summary>
        [GlobalSetup]
        public void CreateDatabase()
        {
            IReadOnlyList<byte[]> images = CoverSeed.CreateImages();
            MeanCoverBytes = images.Average(i => i.Length);
            CheckImageSize(MeanCoverBytes);
            _cover = images[0];

            _databasePath = Path.Combine(Path.GetTempPath(), $"echoplay_bench_cover_{Guid.NewGuid():N}.db");
            using (EchoPlayDbContext seedContext = CreateContext())
            {
                _ = seedContext.Database.EnsureCreated();
                _ = LibrarySeed.Fill(seedContext);
                _episodesWithCover = CoverSeed.Fill(seedContext, images);

                // Die Anwendung legt die Statistik beim Beenden mit PRAGMA optimize an, die
                // Produktivdatei hat sie also. Ohne sie wählte SQLite womöglich einen anderen Plan.
                _ = seedContext.Database.ExecuteSqlRaw("ANALYZE");
            }

            _context = CreateContext();
        }

        /// <summary>Pfad der Messdatenbank, für den Bericht der Abfragepläne.</summary>
        internal string DatabasePath => _databasePath;

        /// <summary>Löscht die Messdatenbank.</summary>
        [GlobalCleanup]
        public void DeleteDatabase()
        {
            _context?.Dispose();
            SqliteConnection.ClearAllPools();
            File.Delete(_databasePath);
        }

        /// <summary>Die Bilddaten aller Folgen der größten Serie in einer Abfrage, wie die Folgenliste sie holt.</summary>
        /// <returns>Die Zahl der gefundenen Cover.</returns>
        [Benchmark]
        public async Task<int> EpisodeCoverBatch()
        {
            CoverImageDataService service = new(Context, _loggers);
            IReadOnlyDictionary<Guid, byte[]> covers = await service
                .GetImageDataByEntitiesAsync(CoverEntityTypes.Episode, _episodesWithCover)
                .ConfigureAwait(false);
            return covers.Count == _episodesWithCover.Count
                ? covers.Count
                : throw new InvalidOperationException($"Erwartet {_episodesWithCover.Count} Cover, gefunden {covers.Count}: Die Messung liefe über den falschen Bestand.");
        }

        /// <summary>Ein Cover in voller Größe dekodieren, wie ohne Angabe einer Zielbreite.</summary>
        /// <returns>Die Zahl der Pixel-Bytes.</returns>
        [Benchmark(Baseline = true)]
        public async Task<int> DecodeFullSize()
        {
            int length = await DecodeAsync(_cover, scaledWidth: null).ConfigureAwait(false);
            return ExpectPixels(length, CoverSeed.CoverEdge);
        }

        /// <summary>Ein Cover gleich beim Dekodieren auf Kachelbreite verkleinern.</summary>
        /// <returns>Die Zahl der Pixel-Bytes.</returns>
        [Benchmark]
        public async Task<int> DecodeTileSize()
        {
            int length = await DecodeAsync(_cover, TileDecodeWidth).ConfigureAwait(false);
            return ExpectPixels(length, TileDecodeWidth);
        }

        private EchoPlayDbContext Context => _context ?? throw new InvalidOperationException("GlobalSetup ist nicht gelaufen.");

        private static async Task<int> DecodeAsync(byte[] data, int? scaledWidth)
        {
            using InMemoryRandomAccessStream stream = new();
            _ = await stream.WriteAsync(data.AsBuffer());
            stream.Seek(0);

            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
            BitmapTransform transform = new();
            if (scaledWidth is int width)
            {
                // Quadratische Cover, Höhe gleich Breite; die Kachel verlangt dasselbe Seitenverhältnis.
                transform.ScaledWidth = (uint)width;
                transform.ScaledHeight = (uint)width;
                transform.InterpolationMode = BitmapInterpolationMode.Fant;
            }

            PixelDataProvider pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            return pixels.DetachPixelData().Length;
        }

        private static int ExpectPixels(int actual, int edge) => actual == edge * edge * 4
            ? actual
            : throw new InvalidOperationException($"Erwartet {edge} × {edge} Pixel, dekodiert {actual / 4}: Das Bild hat nicht die erwartete Größe.");

        /// <summary>
        /// Prüft, dass die Motive im Mittel so groß sind wie die Cover der Produktivbibliothek. Weicht
        /// die Größe um mehr als ein Viertel ab, misst die Reihe etwas anderes als den Bestand.
        /// </summary>
        private static void CheckImageSize(double mean)
        {
            if (Math.Abs(mean - CoverSeed.ProductionMeanBytes) > CoverSeed.ProductionMeanBytes / 4.0)
            {
                throw new InvalidOperationException($"Motive im Mittel {mean / 1024:F0} KB, erwartet um {CoverSeed.ProductionMeanBytes / 1024} KB.");
            }
        }

        private EchoPlayDbContext CreateContext()
        {
            DbContextOptionsBuilder<EchoPlayDbContext> builder = new DbContextOptionsBuilder<EchoPlayDbContext>()
                .UseSqlite($"Data Source={_databasePath}")
                .AddInterceptors(new SqlitePragmaInterceptor())
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            if (Capture is not null)
            {
                _ = builder.AddInterceptors(Capture);
            }

            return new EchoPlayDbContext(builder.Options);
        }
    }
}
