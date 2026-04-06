using EchoPlay.Core.Scoring;
using System;
using System.Collections.Generic;

namespace EchoPlay.Core.Tests.Scoring
{
    /// <summary>
    /// Tests für <see cref="HoerspielAlbumHeuristic"/>.
    /// Prüft die strukturelle Erkennung von Hörspielen anhand von Trackdauern.
    /// Schwellenwerte: min. 1 Track ≥10 min, Durchschnitt ≥5 min, max. 20 Tracks.
    /// </summary>
    public sealed class HoerspielAlbumHeuristicTests
    {
        [Fact]
        public void LooksLikeHoerspiel_ReturnsFalse_WhenEmpty()
        {
            // Leere Trackliste kann kein Hörspiel sein
            IReadOnlyList<TimeSpan> durations = [];

            bool result = HoerspielAlbumHeuristic.LooksLikeHoerspiel(durations);

            Assert.False(result);
        }

        [Fact]
        public void LooksLikeHoerspiel_ReturnsFalse_WhenTooManyTracks()
        {
            // Mehr als 20 Tracks entspricht nicht dem Hörspiel-Muster
            List<TimeSpan> durations = [];
            for (int i = 0; i < 21; i++)
            {
                durations.Add(TimeSpan.FromMinutes(15));
            }

            bool result = HoerspielAlbumHeuristic.LooksLikeHoerspiel(durations);

            Assert.False(result);
        }

        [Fact]
        public void LooksLikeHoerspiel_ReturnsTrue_WhenTypicalHoerspielStructure()
        {
            // Klassische Struktur: wenige lange Tracks wie ein Hörspiel
            IReadOnlyList<TimeSpan> durations =
            [
                TimeSpan.FromMinutes(12),
                TimeSpan.FromMinutes(14),
                TimeSpan.FromMinutes(11)
            ];

            bool result = HoerspielAlbumHeuristic.LooksLikeHoerspiel(durations);

            Assert.True(result);
        }

        [Fact]
        public void LooksLikeHoerspiel_ReturnsFalse_WhenAllTracksVeryShort()
        {
            // Kurze Tracks wie bei einem Musik-Album → kein Hörspiel
            IReadOnlyList<TimeSpan> durations =
            [
                TimeSpan.FromMinutes(3),
                TimeSpan.FromMinutes(4),
                TimeSpan.FromMinutes(3)
            ];

            bool result = HoerspielAlbumHeuristic.LooksLikeHoerspiel(durations);

            Assert.False(result);
        }

        [Fact]
        public void LooksLikeHoerspiel_ReturnsFalse_WhenNoLongTrack()
        {
            // Kein einzelner Track ≥10 min – trotz hohem Durchschnitt kein Hörspiel
            IReadOnlyList<TimeSpan> durations =
            [
                TimeSpan.FromMinutes(9),
                TimeSpan.FromMinutes(9),
                TimeSpan.FromMinutes(9)
            ];

            bool result = HoerspielAlbumHeuristic.LooksLikeHoerspiel(durations);

            Assert.False(result);
        }

        [Fact]
        public void LooksLikeHoerspiel_ReturnsTrue_WithExactlyMaxTracks()
        {
            // Genau 20 Tracks mit gültiger Länge ist noch zulässig
            List<TimeSpan> durations = [];
            for (int i = 0; i < 20; i++)
            {
                durations.Add(TimeSpan.FromMinutes(10));
            }

            bool result = HoerspielAlbumHeuristic.LooksLikeHoerspiel(durations);

            Assert.True(result);
        }

        [Fact]
        public void LooksLikeHoerspiel_ReturnsTrue_WithSingleLongTrack()
        {
            // Ein einziger sehr langer Track (Komplett-Hörspiel ungeteilt)
            IReadOnlyList<TimeSpan> durations = [TimeSpan.FromMinutes(60)];

            bool result = HoerspielAlbumHeuristic.LooksLikeHoerspiel(durations);

            Assert.True(result);
        }

        [Fact]
        public void LooksLikeHoerspiel_ReturnsFalse_WhenAverageTooLow()
        {
            // Ein langer Track, aber viele kurze drücken den Durchschnitt unter die Schwelle
            IReadOnlyList<TimeSpan> durations =
            [
                TimeSpan.FromMinutes(10),
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(1)
            ];

            bool result = HoerspielAlbumHeuristic.LooksLikeHoerspiel(durations);

            Assert.False(result);
        }
    }
}
