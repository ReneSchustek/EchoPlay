using System;
using System.Collections.Generic;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Die Folge, die gerade läuft: ihre Kennung, ihre Spuren und deren Dauern. Daraus ergibt sich
    /// die Stelle in der ganzen Folge, die als Hörstand gespeichert wird.
    /// </summary>
    /// <remarks>
    /// Der Takt der Wiedergabe liest den Zustand von einem anderen Thread als die Oberfläche, die
    /// ihn setzt. Jeder Zugriff läuft deshalb unter einer Sperre.
    /// </remarks>
    internal sealed class PlayingEpisode
    {
        private readonly object _lock = new();
        private Guid _id;
        private IReadOnlyList<string> _trackPaths = [];

        // Die Spurdauern der laufenden Folge. Ohne sie ist nur die Stelle in der laufenden Datei
        // bekannt, und die allein taugt nicht zum Fortsetzen.
        private EpisodeTimeline _timeline = EpisodeTimeline.Empty;

        /// <summary>Die Dateipfade der Spuren in Abspielreihenfolge; leer, wenn nichts läuft.</summary>
        public IReadOnlyList<string> TrackPaths
        {
            get { lock (_lock) { return _trackPaths; } }
        }

        /// <summary>Die Gesamtdauer der Folge über alle Spuren; <see cref="TimeSpan.Zero"/>, wenn unbekannt.</summary>
        public TimeSpan TotalDuration
        {
            get { lock (_lock) { return _timeline.TotalDuration; } }
        }

        /// <summary>Merkt sich eine neu gestartete Folge.</summary>
        /// <param name="id">Kennung der Folge, <see cref="Guid.Empty"/> für eine Ordner-Wiedergabe.</param>
        /// <param name="trackPaths">Dateipfade der Spuren.</param>
        /// <param name="timeline">Die Spurdauern, sofern bekannt.</param>
        public void Start(Guid id, IReadOnlyList<string> trackPaths, EpisodeTimeline timeline)
        {
            lock (_lock)
            {
                _id = id;
                _trackPaths = [.. trackPaths];
                _timeline = timeline;
            }
        }

        /// <summary>Vergisst die laufende Folge.</summary>
        public void Clear() => Start(Guid.Empty, [], EpisodeTimeline.Empty);

        /// <summary>Rechnet die Stelle in einer Spur in die Stelle in der ganzen Folge um.</summary>
        /// <param name="trackIndex">Die laufende Spur, nullbasiert.</param>
        /// <param name="positionInTrack">Die Stelle in dieser Spur.</param>
        /// <returns>Die Stelle in der Folge; ohne bekannte Dauern die Stelle in der Spur.</returns>
        public TimeSpan ToOverall(int trackIndex, TimeSpan positionInTrack)
        {
            lock (_lock) { return _timeline.ToOverall(trackIndex, positionInTrack); }
        }

        /// <summary>
        /// Kennung und Stelle in der Folge in einem Zug, damit beide zur selben Folge gehören.
        /// Gespeichert wird die Stelle in der Folge, nicht die in der Datei, sonst setzt die
        /// Wiedergabe später in der falschen Spur wieder ein.
        /// </summary>
        /// <param name="trackIndex">Die laufende Spur, nullbasiert.</param>
        /// <param name="positionInTrack">Die Stelle in dieser Spur.</param>
        /// <returns>Kennung der Folge und Stelle in der ganzen Folge.</returns>
        public (Guid Id, TimeSpan Position) Snapshot(int trackIndex, TimeSpan positionInTrack)
        {
            lock (_lock) { return (_id, _timeline.ToOverall(trackIndex, positionInTrack)); }
        }
    }
}
