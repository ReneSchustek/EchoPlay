using System.IO;

namespace EchoPlay.LocalLibrary.Tests.Infrastructure
{
    /// <summary>
    /// Erzeugt minimale, gültige Audiodateien für Tests.
    /// </summary>
    /// <remarks>
    /// Die Bytes stehen hier im Quelltext statt als Datei im Repository: Eine echte
    /// Hörspieldatei wäre urheberrechtlich belastet, und eine Beispieldatei im Repository
    /// müsste bei jedem Auschecken mitkommen. Das Minimum, das TagLib zum Lesen und
    /// Schreiben braucht, sind ein ID3-Kopf und ein einzelner stiller MPEG-Rahmen.
    /// </remarks>
    internal static class AudioTestFiles
    {
        /// <summary>Legt eine minimale MP3-Datei an und liefert ihren Pfad.</summary>
        /// <param name="folder">Zielordner.</param>
        /// <param name="fileName">Dateiname inklusive Endung.</param>
        /// <returns>Der vollständige Pfad der angelegten Datei.</returns>
        public static string CreateMp3(string folder, string fileName = "01 - Teil 1.mp3")
        {
            string path = Path.Combine(folder, fileName);
            File.WriteAllBytes(path, BuildMinimalMp3());
            return path;
        }

        /// <summary>Legt eine MP3-Datei mit eingebettetem Titelbild an.</summary>
        /// <param name="folder">Zielordner.</param>
        /// <param name="coverBytes">Die einzubettenden Bilddaten.</param>
        /// <param name="fileName">Dateiname inklusive Endung.</param>
        /// <returns>Der vollständige Pfad der angelegten Datei.</returns>
        public static string CreateMp3WithCover(
            string folder, byte[] coverBytes, string fileName = "01 - Mit Bild.mp3")
        {
            string path = CreateMp3(folder, fileName);

            using (TagLib.File file = TagLib.File.Create(path))
            {
                file.Tag.Pictures =
                [
                    new TagLib.Picture(new TagLib.ByteVector(coverBytes))
                    {
                        Type = TagLib.PictureType.FrontCover,
                        MimeType = "image/jpeg",
                    },
                ];
                file.Save();
            }

            return path;
        }

        /// <summary>Legt eine MP3-Datei mit Titel-, Album- und Spurangabe an.</summary>
        /// <param name="folder">Zielordner.</param>
        /// <param name="title">Der Titel.</param>
        /// <param name="album">Das Album.</param>
        /// <param name="trackNumber">Die Spurnummer.</param>
        /// <param name="fileName">Dateiname inklusive Endung.</param>
        /// <returns>Der vollständige Pfad der angelegten Datei.</returns>
        public static string CreateMp3WithTags(
            string folder,
            string title,
            string album,
            uint trackNumber,
            string fileName = "01 - Beschriftet.mp3")
        {
            string path = CreateMp3(folder, fileName);

            using (TagLib.File file = TagLib.File.Create(path))
            {
                file.Tag.Title = title;
                file.Tag.Album = album;
                file.Tag.Track = trackNumber;
                file.Save();
            }

            return path;
        }

        private static byte[] BuildMinimalMp3()
        {
            // ID3v2.3-Kopf (10 Bytes) und ein TIT2-Rahmen (20 Bytes).
            byte[] id3 =
            [
                0x49, 0x44, 0x33,
                0x03, 0x00,
                0x00,
                0x00, 0x00, 0x00, 0x14,

                0x54, 0x49, 0x54, 0x32,
                0x00, 0x00, 0x00, 0x0A,
                0x00, 0x00,
                0x00,
                0x54, 0x65, 0x73, 0x74,
                0x54, 0x69, 0x74, 0x6C, 0x65,
            ];

            // MPEG1 Layer 3, 128 kbit/s, 44100 Hz, Stereo — 417 Bytes, komplett still.
            byte[] frame = new byte[417];
            frame[0] = 0xFF;
            frame[1] = 0xFB;
            frame[2] = 0x90;
            frame[3] = 0x00;

            byte[] result = new byte[id3.Length + frame.Length];
            id3.CopyTo(result, 0);
            frame.CopyTo(result, id3.Length);
            return result;
        }
    }
}
