using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Analysiert die Helligkeit eines Cover-Bildes in der oberen linken Ecke.
    /// Statische Utility-Klasse – die WinRT-COM-Typen
    /// (<see cref="BitmapDecoder"/>, <see cref="InMemoryRandomAccessStream"/>)
    /// werden nur bei tatsächlichem Aufruf instanziiert, damit Unit-Tests ohne
    /// WinUI-Hosting nicht beim Laden des ViewModels eine COM-Exception auslösen.
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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cover-Helligkeitsanalyse via WinRT BitmapDecoder: kaputte JPEG/PNG-Header, unsupportete Pixel-Formate oder COM-Fehler aus dem WIC-Stack werden zu 'null' normalisiert – der Aufrufer behandelt das als 'Helligkeit unbekannt'.")]
        public static async Task<bool?> AnalyzeBrightnessFromBytesAsync(byte[] coverBytes)
        {
            try
            {
                using InMemoryRandomAccessStream stream = new();
                _ = await stream.WriteAsync(coverBytes.AsBuffer());
                stream.Seek(0);

                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);

                // Nur die obere linke Ecke samplen (30×30 px oder kleiner)
                uint sampleWidth = Math.Min(30, decoder.PixelWidth);
                uint sampleHeight = Math.Min(30, decoder.PixelHeight);

                BitmapTransform transform = new()
                {
                    Bounds = new BitmapBounds
                    {
                        X = 0,
                        Y = 0,
                        Width = sampleWidth,
                        Height = sampleHeight
                    }
                };

                PixelDataProvider pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Ignore,
                    transform,
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                byte[] pixels = pixelData.DetachPixelData();

                // Wahrgenommene Helligkeit: 0.299*R + 0.587*G + 0.114*B (BGRA-Format)
                long totalBrightness = 0;
                int pixelCount = pixels.Length / 4;

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    totalBrightness += (int)(pixels[i + 2] * 0.299 + pixels[i + 1] * 0.587 + pixels[i] * 0.114);
                }

                double average = pixelCount > 0 ? (double)totalBrightness / pixelCount : 128;
                return average > 128;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
