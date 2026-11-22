using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Analysiert die Helligkeit eines Cover-Bildes in der oberen linken Ecke.
    /// Statische Utility-Klasse: <see cref="BitmapDecoder"/> und
    /// <see cref="InMemoryRandomAccessStream"/> gehören zum Betriebssystem, sind
    /// systemweit registriert und laufen auch ohne WinUI-Hosting. Die Klasse ist
    /// deshalb vollständig durch Unit-Tests abgedeckt.
    /// </summary>
    public static class CoverBrightnessAnalyzer
    {
        /// <summary>
        /// Analysiert die durchschnittliche Helligkeit im Bereich oben links (30×30 px)
        /// eines bereits heruntergeladenen Cover-Bildes.
        /// </summary>
        /// <param name="coverBytes">Die rohen Bilddaten (JPEG, PNG o.ä.).</param>
        /// <returns>
        /// <see langword="true"/> wenn der Bereich hell ist (Helligkeit > 128),
        /// <see langword="false"/> wenn dunkel. Null bei Fehler.
        /// </returns>
        /// <exception cref="ArgumentNullException">Wenn <paramref name="coverBytes"/> null ist.</exception>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cover-Helligkeitsanalyse via WinRT BitmapDecoder: kaputte JPEG/PNG-Header, unsupportete Pixel-Formate oder COM-Fehler aus dem WIC-Stack werden zu 'null' normalisiert – der Aufrufer behandelt das als 'Helligkeit unbekannt'.")]
        public static async Task<bool?> AnalyzeBrightnessFromBytesAsync(byte[] coverBytes)
        {
            // Guard vor dem try: ohne ihn wäre die NullReferenceException aus AsBuffer()
            // vom CA1031-Catch zu "Helligkeit unbekannt" verschluckt worden, und der Aufrufer
            // hätte den Programmierfehler nie gesehen.
            ArgumentNullException.ThrowIfNull(coverBytes);

            try
            {
                using InMemoryRandomAccessStream stream = new();
                _ = await stream.WriteAsync(coverBytes.AsBuffer());
                stream.Seek(0);

                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
                return IsBright(await ReadTopLeftCornerAsync(decoder));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Liest die Pixel der linken oberen Ecke als BGRA.
        /// </summary>
        private static async Task<byte[]> ReadTopLeftCornerAsync(BitmapDecoder decoder)
        {
            // Nur die Ecke oben links, denn dort liegt das Auswahlkästchen der Suchkachel, dessen
            // Rahmenfarbe sich nach diesem Ergebnis richtet. 30 × 30 Pixel decken das Kästchen ab.
            BitmapTransform transform = new()
            {
                Bounds = new BitmapBounds
                {
                    X = 0,
                    Y = 0,
                    Width = Math.Min(30, decoder.PixelWidth),
                    Height = Math.Min(30, decoder.PixelHeight)
                }
            };

            PixelDataProvider pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            return pixelData.DetachPixelData();
        }

        /// <summary>
        /// Ob die mittlere wahrgenommene Helligkeit der BGRA-Pixel über der Skalenmitte liegt.
        /// </summary>
        private static bool IsBright(byte[] pixels)
        {
            // Wahrgenommene Helligkeit nach ITU-R BT.601: 0.299*R + 0.587*G + 0.114*B. Die Bytes
            // liegen als BGRA vor, deshalb steht Rot an Stelle 2 und Blau an Stelle 0.
            long totalBrightness = 0;
            int pixelCount = pixels.Length / 4;

            for (int i = 0; i < pixels.Length; i += 4)
            {
                totalBrightness += (int)(pixels[i + 2] * 0.299 + pixels[i + 1] * 0.587 + pixels[i] * 0.114);
            }

            // 128 ist die Mitte der Skala 0 bis 255; ohne Pixel gilt das Bild damit als dunkel.
            double average = pixelCount > 0 ? (double)totalBrightness / pixelCount : 128;
            return average > 128;
        }
    }
}
