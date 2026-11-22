using EchoPlay.Data.Context;
using EchoPlay.Data.Entities.Library;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace EchoPlay.Benchmarks.Cases.Infrastructure
{
    /// <summary>
    /// Erzeugt Cover-Bilder in Größe und Format der Produktivbibliothek aus einem festen Startwert
    /// und legt sie zu Serien und Folgen in die Messdatenbank.
    /// </summary>
    /// <remarks>
    /// Ein Cover der Anbieter misst 600 × 600 Pixel und liegt als JPEG vor, im Mittel 255 KB. Ein
    /// Verlauf mit Rauschen kommt beim Kodieren auf eine ähnliche Größe; ein einfarbiges Bild wäre
    /// wenige Kilobyte groß und machte jede Messung zu schnell. Erzeugt werden nur wenige
    /// verschiedene Bilder, die sich über die Zeilen wiederholen: SQLite speichert jede Zeile
    /// einzeln, für Datei und Abfrage zählt also die Zahl der Zeilen, nicht die Zahl der Motive.
    /// </remarks>
    internal static class CoverSeed
    {
        /// <summary>Kantenlänge eines Covers in Pixel, wie die Anbieter es liefern.</summary>
        public const int CoverEdge = 600;

        /// <summary>Mittlere Größe eines Covers im Abzug der Produktivbibliothek, in Byte.</summary>
        public const int ProductionMeanBytes = 255 * 1024;

        /// <summary>
        /// Zahl der Cover im Abzug der Produktivbibliothek. Sie zählt, nicht nur die der abgefragten
        /// Folgen: Der Plan des Stapelzugriffs geht alle Cover einer Art durch, und mit wenigen Zeilen
        /// wäre die Messung schneller als der Betrieb.
        /// </summary>
        public const int ProductionCoverCount = 5194;

        /// <summary>Startwert der Bilder; ändert er sich, ist die Messreihe gebrochen.</summary>
        public const int Seed = 20261121;

        /// <summary>Zahl der verschiedenen Motive.</summary>
        private const int DistinctImages = 8;

        /// <summary>
        /// Stärke des Rauschens über dem Verlauf, in Helligkeitsstufen. Gewählt, damit die Bilder
        /// beim Kodieren im Mittel bei der Größe der Produktivbibliothek landen.
        /// </summary>
        private const int NoiseAmplitude = 84;

        /// <summary>
        /// Erzeugt die Motive als JPEG.
        /// </summary>
        /// <returns>Die kodierten Bilder.</returns>
        public static IReadOnlyList<byte[]> CreateImages()
        {
            SeededSequence random = new(Seed);
            List<byte[]> images = new(DistinctImages);
            for (int i = 0; i < DistinctImages; i++)
            {
                images.Add(Encode(CreatePixels(random)));
            }

            return images;
        }

        /// <summary>
        /// Legt Cover in der Zahl der Produktivbibliothek an: eins je Serie, die übrigen für Folgen,
        /// zuerst alle der größten Serie.
        /// </summary>
        /// <param name="context">Kontext auf einer Datenbank mit dem Bestand aus <see cref="LibrarySeed"/>.</param>
        /// <param name="images">Die Motive aus <see cref="CreateImages"/>.</param>
        /// <returns>Die Kennungen der Folgen der größten Serie, alle mit Cover.</returns>
        public static IReadOnlyList<Guid> Fill(EchoPlayDbContext context, IReadOnlyList<byte[]> images)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(images);

            List<Guid> seriesIds = [.. context.Series.Select(s => s.Id)];
            (List<Guid> largestEpisodes, List<Guid> episodeIds) =
                SelectEpisodes(context, ProductionCoverCount - seriesIds.Count);

            int next = 0;
            foreach (Guid id in seriesIds)
            {
                _ = context.CoverImages.Add(Cover(CoverEntityTypes.Series, id, images[next++ % images.Count]));
            }

            // In Blöcken speichern, sonst hält der Kontext ein Gigabyte Bilddaten auf einmal.
            foreach (Guid[] chunk in episodeIds.Chunk(500))
            {
                foreach (Guid id in chunk)
                {
                    _ = context.CoverImages.Add(Cover(CoverEntityTypes.Episode, id, images[next++ % images.Count]));
                }

                _ = context.SaveChanges();
                context.ChangeTracker.Clear();
            }

            _ = context.SaveChanges();
            context.ChangeTracker.Clear();
            return largestEpisodes;
        }

        /// <summary>
        /// Wählt die Folgen, die ein Cover bekommen: zuerst alle der größten Serie, denn ihre
        /// Folgenliste ist die längste, die eine Seite auf einmal lädt; danach die übrigen nach
        /// Serie und Nummer.
        /// </summary>
        /// <param name="context">Kontext auf dem Bestand aus <see cref="LibrarySeed"/>.</param>
        /// <param name="count">Wie viele Folgen insgesamt ein Cover bekommen.</param>
        /// <returns>Die Folgen der größten Serie und alle ausgewählten Folgen.</returns>
        private static (List<Guid> Largest, List<Guid> Selected) SelectEpisodes(EchoPlayDbContext context, int count)
        {
            Guid largest = context.Episodes
                .GroupBy(e => e.SeriesId)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .First();
            List<Guid> largestEpisodes = [.. context.Episodes.Where(e => e.SeriesId == largest).Select(e => e.Id)];
            IEnumerable<Guid> otherEpisodes = context.Episodes
                .Where(e => e.SeriesId != largest)
                .OrderBy(e => e.SeriesId)
                .ThenBy(e => e.EpisodeNumber)
                .Select(e => e.Id);
            return (largestEpisodes, [.. largestEpisodes.Concat(otherEpisodes).Take(count)]);
        }

        private static CoverImage Cover(string entityType, Guid entityId, byte[] data) => new()
        {
            EntityType = entityType,
            EntityId = entityId,
            ImageData = data
        };

        /// <summary>Ein diagonaler Farbverlauf mit Rauschen, als BGRA.</summary>
        private static byte[] CreatePixels(SeededSequence random)
        {
            byte[] pixels = new byte[CoverEdge * CoverEdge * 4];
            int baseBlue = random.Next(256);
            int baseGreen = random.Next(256);
            int baseRed = random.Next(256);

            for (int y = 0; y < CoverEdge; y++)
            {
                for (int x = 0; x < CoverEdge; x++)
                {
                    int offset = ((y * CoverEdge) + x) * 4;
                    int ramp = (x + y) * 255 / (2 * CoverEdge);
                    pixels[offset] = Channel(baseBlue + ramp, random);
                    pixels[offset + 1] = Channel(baseGreen - ramp, random);
                    pixels[offset + 2] = Channel(baseRed + (ramp / 2), random);
                    pixels[offset + 3] = 255;
                }
            }

            return pixels;
        }

        private static byte Channel(int value, SeededSequence random)
        {
            int noisy = (value & 0xFF) + random.Next((2 * NoiseAmplitude) + 1) - NoiseAmplitude;
            return (byte)Math.Clamp(noisy, 0, 255);
        }

        private static byte[] Encode(byte[] pixels)
        {
            using InMemoryRandomAccessStream stream = new();
            BitmapEncoder encoder = BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream).AsTask().GetAwaiter().GetResult();
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                CoverEdge,
                CoverEdge,
                96,
                96,
                pixels);
            encoder.FlushAsync().AsTask().GetAwaiter().GetResult();

            byte[] data = new byte[stream.Size];
            stream.Seek(0);
            using DataReader reader = new(stream.GetInputStreamAt(0));
            _ = reader.LoadAsync((uint)stream.Size).AsTask().GetAwaiter().GetResult();
            reader.ReadBytes(data);
            return data;
        }
    }
}
