using EchoPlay.App.Services;
using System;
using System.Collections.Generic;
using Xunit;

namespace EchoPlay.App.Tests.Services
{
    /// <summary>
    /// Tests für <see cref="EpisodeTimeline"/> — die Umrechnung zwischen der Stelle in
    /// einer Spur und der Stelle in der ganzen Folge.
    /// </summary>
    /// <remarks>
    /// Der Fehler, den sie verhindern: Gespeichert wurde die Position in der laufenden
    /// Datei, fortgesetzt wurde sie auf Spur 1. Wer bei Spur 3, Minute 10 aufhörte, landete
    /// bei Spur 1, Minute 10 — bei einer vierstündigen Folge über zwei Stunden zu früh.
    /// </remarks>
    public sealed class EpisodeTimelineTests
    {
        private static readonly IReadOnlyList<TimeSpan> DreiSpuren =
        [
            TimeSpan.FromMinutes(60),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(45)
        ];

        [Fact]
        public void TotalDuration_IsSumOfTracks()
        {
            EpisodeTimeline timeline = new(DreiSpuren);

            Assert.Equal(TimeSpan.FromMinutes(135), timeline.TotalDuration);
        }

        [Fact]
        public void ToOverall_ThirdTrack_AddsPrecedingTracks()
        {
            EpisodeTimeline timeline = new(DreiSpuren);

            TimeSpan overall = timeline.ToOverall(2, TimeSpan.FromMinutes(10));

            Assert.Equal(TimeSpan.FromMinutes(100), overall);
        }

        [Fact]
        public void ToOverall_FirstTrack_IsUnchanged()
        {
            EpisodeTimeline timeline = new(DreiSpuren);

            Assert.Equal(TimeSpan.FromMinutes(10), timeline.ToOverall(0, TimeSpan.FromMinutes(10)));
        }

        [Fact]
        public void ToTrack_FindsTrackAndOffset()
        {
            EpisodeTimeline timeline = new(DreiSpuren);

            (int index, TimeSpan position) = timeline.ToTrack(TimeSpan.FromMinutes(100));

            Assert.Equal(2, index);
            Assert.Equal(TimeSpan.FromMinutes(10), position);
        }

        [Fact]
        public void ToTrack_And_ToOverall_AreInverse()
        {
            // Der eigentliche Vertrag: Was gespeichert wird, muss dort wieder ankommen.
            EpisodeTimeline timeline = new(DreiSpuren);

            TimeSpan overall = timeline.ToOverall(1, TimeSpan.FromMinutes(7));
            (int index, TimeSpan position) = timeline.ToTrack(overall);

            Assert.Equal(1, index);
            Assert.Equal(TimeSpan.FromMinutes(7), position);
        }

        [Fact]
        public void ToTrack_ExactTrackBoundary_StartsNextTrack()
        {
            EpisodeTimeline timeline = new(DreiSpuren);

            (int index, TimeSpan position) = timeline.ToTrack(TimeSpan.FromMinutes(60));

            Assert.Equal(1, index);
            Assert.Equal(TimeSpan.Zero, position);
        }

        [Fact]
        public void ToTrack_BeyondEnd_StaysInsideTheList()
        {
            EpisodeTimeline timeline = new(DreiSpuren);

            (int index, TimeSpan position) = timeline.ToTrack(TimeSpan.FromHours(10));

            Assert.Equal(2, index);
            Assert.Equal(TimeSpan.Zero, position);
        }

        [Fact]
        public void WithoutDurations_BehavesLikeBefore()
        {
            // Ohne bekannte Spurdauern bleibt es beim bisherigen Verhalten: Die Stelle gilt
            // für die laufende Spur, es wird nicht umgerechnet.
            EpisodeTimeline timeline = EpisodeTimeline.Empty;

            Assert.False(timeline.IsKnown);
            Assert.Equal(TimeSpan.Zero, timeline.TotalDuration);
            Assert.Equal(TimeSpan.FromMinutes(10), timeline.ToOverall(2, TimeSpan.FromMinutes(10)));

            (int index, TimeSpan position) = timeline.ToTrack(TimeSpan.FromMinutes(10));
            Assert.Equal(0, index);
            Assert.Equal(TimeSpan.FromMinutes(10), position);
        }

        [Fact]
        public void UnknownTrackDuration_StopsTheCalculation()
        {
            // Eine Spur ohne Dauer lässt sich nicht überspringen — dort endet die Rechnung,
            // statt den Rest ins Leere laufen zu lassen.
            EpisodeTimeline timeline = new([TimeSpan.FromMinutes(30), TimeSpan.Zero, TimeSpan.FromMinutes(20)]);

            (int index, TimeSpan position) = timeline.ToTrack(TimeSpan.FromMinutes(45));

            Assert.Equal(1, index);
            Assert.Equal(TimeSpan.FromMinutes(15), position);
        }
    }
}
