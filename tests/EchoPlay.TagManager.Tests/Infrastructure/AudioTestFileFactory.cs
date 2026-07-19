using System.Threading;

namespace EchoPlay.TagManager.Tests.Infrastructure
{
    /// <summary>
    /// Erzeugt minimale, gültige Audiodateien als temporäre Dateien für Tests.
    /// Die Binärdaten sind hardcodiert – keine echten Audioinhalte, nur das Minimum,
    /// das TagLib# für das Lesen und Schreiben von Metadaten benötigt.
    /// </summary>
    internal static class AudioTestFileFactory
    {
        private static int _fileCounter;

        /// <summary>
        /// Minimales MP3: ID3v2.3-Header mit einem TIT2-Frame ("TestTitle") gefolgt von einem
        /// stillen MPEG1-Layer3-Frame (128 kbps, 44100 Hz, Stereo, 417 Bytes).
        /// TagLib# benötigt beim Schreiben von Tags mindestens einen gültigen MPEG-Frame,
        /// um die Dateistruktur korrekt aufzubauen.
        /// </summary>
        private static readonly byte[] MinimalMp3Data = BuildMinimalMp3();

        private static byte[] BuildMinimalMp3()
        {
            // ID3v2.3-Header (10 Bytes) + TIT2-Frame (20 Bytes) = 30 Bytes Tag
            byte[] id3 =
            [
                0x49, 0x44, 0x33,             // "ID3"
                0x03, 0x00,                   // Version 2.3.0
                0x00,                         // Kein Extended Header, keine Footer
                0x00, 0x00, 0x00, 0x14,       // Syncsafe-Größe = 20 Bytes (Inhalt nach Header)

                0x54, 0x49, 0x54, 0x32,       // Frame-ID "TIT2"
                0x00, 0x00, 0x00, 0x0A,       // Frame-Größe = 10 Bytes
                0x00, 0x00,                   // Keine Frame-Flags
                0x00,                         // Textkodierung: ISO-8859-1
                0x54, 0x65, 0x73, 0x74,       // "Test"
                0x54, 0x69, 0x74, 0x6C, 0x65  // "Title"
            ];

            // MPEG1, Layer 3, 128 kbps, 44100 Hz, Stereo, kein Padding
            // Frame-Header: 0xFF 0xFB 0x90 0x00
            // Frame-Größe: int(144 × 128000 / 44100) = 417 Bytes
            // Side-Info (Stereo): 32 Bytes, Rest: Null-Bytes = stille Audiodaten
            byte[] mpegFrame = new byte[417];
            mpegFrame[0] = 0xFF;
            mpegFrame[1] = 0xFB;
            mpegFrame[2] = 0x90;
            mpegFrame[3] = 0x00;
            // Bytes 4–36: Side-Info (32 Bytes), alle Null = kein Huffman-kodierter Inhalt
            // Bytes 37–416: Hauptdaten (alle Null = Stille)

            byte[] result = new byte[id3.Length + mpegFrame.Length];
            id3.CopyTo(result, 0);
            mpegFrame.CopyTo(result, id3.Length);
            return result;
        }

        /// <summary>
        /// Minimales FLAC: "fLaC"-Signatur + STREAMINFO-Metadatenblock (42 Bytes).
        /// TagLib# erkennt FLAC-Dateien anhand der ersten 4 Bytes und liest dann
        /// den Vorbis-Comment-Tag. Ohne Audio-Frames hat die Datei Dauer 0, aber Tags funktionieren.
        /// </summary>
        private static readonly byte[] MinimalFlacData =
        [
            // FLAC-Signatur (4 Bytes)
            0x66, 0x4C, 0x61, 0x43, // "fLaC"

            // STREAMINFO-Metadatenblock-Header (4 Bytes)
            // Bit 7 = 1 (letzter Metadatenblock), Bits 6-0 = 0 (Typ STREAMINFO)
            // Blockgröße = 34 Bytes
            0x80, 0x00, 0x00, 0x22,

            // STREAMINFO-Daten (34 Bytes)
            0x10, 0x00,                   // Minimale Blockgröße = 4096 Samples
            0x10, 0x00,                   // Maximale Blockgröße = 4096 Samples
            0x00, 0x00, 0x00,             // Minimale Framegröße = 0 (unbekannt)
            0x00, 0x00, 0x00,             // Maximale Framegröße = 0 (unbekannt)

            // Samplerate (20 Bit) + Kanäle-1 (3 Bit) + BPS-1 (5 Bit) + Samples gesamt (36 Bit)
            // Samplerate 44100 Hz = 0x0AC44 in 20 Bit: 0000 1010 1100 0100 0100
            // Kanäle = 1 (Mono), Kanäle-1 = 0: 000
            // BPS = 16, BPS-1 = 15: 01111
            // Gesamtsamples = 0: 36 × 0
            0x0A, 0xC4, 0x40, 0xF0, 0x00, 0x00, 0x00, 0x00,

            // MD5-Prüfsumme des unkomprimierten Audio (16 Bytes, alle 0 = leer/unbekannt)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ];

        /// <summary>
        /// Erstellt eine temporäre MP3-Datei aus den minimalen Binärdaten und gibt den Pfad zurück.
        /// Der Aufrufer ist für das Löschen der Datei nach dem Test verantwortlich.
        /// </summary>
        /// <returns>Absoluter Pfad zur temporären MP3-Datei.</returns>
        public static string CreateTempMp3()
        {
            int id = Interlocked.Increment(ref _fileCounter);
            string path = Path.Combine(Path.GetTempPath(), $"echoplay_test_{id:D6}.mp3");
            File.WriteAllBytes(path, MinimalMp3Data);
            return path;
        }

        /// <summary>
        /// Erstellt eine temporäre FLAC-Datei aus den minimalen Binärdaten und gibt den Pfad zurück.
        /// Der Aufrufer ist für das Löschen der Datei nach dem Test verantwortlich.
        /// </summary>
        /// <returns>Absoluter Pfad zur temporären FLAC-Datei.</returns>
        public static string CreateTempFlac()
        {
            int id = Interlocked.Increment(ref _fileCounter);
            string path = Path.Combine(Path.GetTempPath(), $"echoplay_test_{id:D6}.flac");
            File.WriteAllBytes(path, MinimalFlacData);
            return path;
        }

        /// <summary>
        /// Erstellt eine temporäre Datei mit einem nicht unterstützten Format (.xyz).
        /// Wird für Tests verwendet, die das Verhalten bei unbekannten Dateiformaten prüfen.
        /// </summary>
        /// <returns>Absoluter Pfad zur temporären Datei.</returns>
        public static string CreateTempUnsupportedFile()
        {
            int id = Interlocked.Increment(ref _fileCounter);
            string path = Path.Combine(Path.GetTempPath(), $"echoplay_test_{id:D6}.xyz");
            File.WriteAllBytes(path, [0x00, 0x01, 0x02]);
            return path;
        }
    }
}
