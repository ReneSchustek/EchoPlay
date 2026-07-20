using EchoPlay.Core.Models;
using EchoPlay.LocalLibrary.Matching;
using System;
using System.Collections.Generic;
using System.IO;

namespace EchoPlay.LocalLibrary.Tests.Matching
{
    /// <summary>
    /// Ergänzende Tests für Grenzfälle und <see cref="CustomMatchHintWriter.WriteHintFile"/>.
    /// </summary>
    public sealed class TrackMatcherEdgeCaseTests : IDisposable
    {
        private readonly TrackMatcher _matcher = new();
        private readonly string _tempDir;

        /// <summary>
        /// Erstellt ein temporäres Verzeichnis für Datei-Tests.
        /// </summary>
        public TrackMatcherEdgeCaseTests()
        {
            _tempDir = Directory.CreateTempSubdirectory("echoplay_matcher_").FullName;
        }

        /// <summary>
        /// Räumt das temporäre Verzeichnis auf.
        /// </summary>
        public void Dispose()
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        [Fact]
        public void Classify_ExactlyTwentyTracks_ReturnsTbT()
        {
            // Grenzwert: genau 20 Tracks auf beiden Seiten → TbT
            TrackMatchKind result = _matcher.Classify(localTrackCount: 20, onlineTrackCount: 20);

            Assert.Equal(TrackMatchKind.TbT, result);
        }

        [Fact]
        public void Classify_TwentyOneEqualTracks_ReturnsStreaming()
        {
            // 21 = 21: beide > 20 → Streaming (Grenzwert exakt oberhalb von 20)
            TrackMatchKind result = _matcher.Classify(localTrackCount: 21, onlineTrackCount: 21);

            Assert.Equal(TrackMatchKind.Streaming, result);
        }

        [Fact]
        public void Classify_UnequalAboveThreshold_ReturnsStreaming()
        {
            // Unterschiedliche Zahlen, beide > 20 → Streaming
            TrackMatchKind result = _matcher.Classify(localTrackCount: 25, onlineTrackCount: 30);

            Assert.Equal(TrackMatchKind.Streaming, result);
        }

        [Fact]
        public void Classify_SingleTrackMatch_ReturnsTbT()
        {
            // 1 lokal = 1 online (ungekürzte Komplett-Fassung als eine Datei)
            TrackMatchKind result = _matcher.Classify(localTrackCount: 1, onlineTrackCount: 1);

            Assert.Equal(TrackMatchKind.TbT, result);
        }

        [Fact]
        public void WriteCustomHintFile_CreatesFile_WithNumberedLines()
        {
            // Die Hint-Datei muss nummerierte Tracknamen enthalten
            IReadOnlyList<string> trackNames = ["Intro", "Hauptteil", "Abspann"];

            CustomMatchHintWriter.WriteHintFile(_tempDir, trackNames);

            string filePath = Path.Combine(_tempDir, "expected_tracks.txt");
            string[] lines = File.ReadAllLines(filePath);

            Assert.Equal(3, lines.Length);
            Assert.Equal("1 Intro", lines[0]);
            Assert.Equal("2 Hauptteil", lines[1]);
            Assert.Equal("3 Abspann", lines[2]);
        }

        [Fact]
        public void WriteCustomHintFile_CreatesFile_EvenForEmptyList()
        {
            // Auch eine leere Trackliste darf nicht zu einem Fehler führen
            CustomMatchHintWriter.WriteHintFile(_tempDir, []);

            string filePath = Path.Combine(_tempDir, "expected_tracks.txt");
            Assert.True(File.Exists(filePath));
            Assert.Empty(File.ReadAllLines(filePath));
        }
    }
}
