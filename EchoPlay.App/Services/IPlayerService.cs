using System;
using System.Collections.Generic;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Definiert den Vertrag für den zentralen Wiedergabe-Service.
    /// Ermöglicht die Entkopplung von ViewModels und Tests von der konkreten <see cref="PlayerService"/>-Implementierung.
    /// </summary>
    public interface IPlayerService
    {
        /// <summary>
        /// Wird ausgelöst, wenn sich Abspielstatus, Track oder Position geändert haben.
        /// </summary>
        event EventHandler? StateChanged;

        /// <summary>Gibt an, ob gerade Wiedergabe aktiv ist.</summary>
        bool IsPlaying { get; }

        /// <summary>
        /// Titel des aktuell laufenden Tracks (Dateiname ohne Erweiterung).
        /// Null, wenn nichts spielt.
        /// </summary>
        string? CurrentTrackTitle { get; }

        /// <summary>Aktuelle Abspielposition.</summary>
        TimeSpan Position { get; }

        /// <summary>Gesamtdauer des aktuell laufenden Tracks.</summary>
        TimeSpan Duration { get; }

        /// <summary>
        /// Wiedergabegeschwindigkeit. 1.0 entspricht normaler Geschwindigkeit.
        /// Gültige Werte: 0.25 bis 4.0 (Plattformlimit des MediaPlayer).
        /// </summary>
        double PlaybackRate { get; set; }

        /// <summary>
        /// Verbleibende Zeit des Einschlaf-Timers.
        /// Null, wenn kein Timer aktiv ist.
        /// </summary>
        TimeSpan? SleepTimerRemaining { get; }

        /// <summary>
        /// Startet die Wiedergabe einer Trackliste ab dem angegebenen Index.
        /// </summary>
        /// <param name="episodeId">ID der Episode – für PlaybackState-Persistenz.</param>
        /// <param name="trackPaths">Absolute Dateipfade der Audiotracks, in Reihenfolge.</param>
        /// <param name="startIndex">Index des ersten Tracks (0-basiert).</param>
        /// <param name="resumePosition">Position, ab der fortgesetzt werden soll.</param>
        void Play(Guid episodeId, IReadOnlyList<string> trackPaths, int startIndex = 0, TimeSpan resumePosition = default);

        /// <summary>Pausiert die Wiedergabe.</summary>
        void Pause();

        /// <summary>
        /// Stoppt die Wiedergabe vollständig, speichert die aktuelle Position
        /// und leert die Playlist. Der MiniPlayer wird dadurch ausgeblendet,
        /// weil <see cref="CurrentTrackTitle"/> auf null gesetzt wird.
        /// </summary>
        void Stop();

        /// <summary>Setzt eine pausierte Wiedergabe fort.</summary>
        void Resume();

        /// <summary>Springt zum nächsten Track in der Wiedergabeliste.</summary>
        void SkipToNext();

        /// <summary>Springt zum vorherigen Track in der Wiedergabeliste.</summary>
        void SkipToPrevious();

        /// <summary>
        /// Springt zu einer bestimmten Position im aktuellen Track.
        /// </summary>
        /// <param name="position">Die Zielposition.</param>
        void SeekTo(TimeSpan position);

        /// <summary>
        /// Setzt oder deaktiviert den Einschlaf-Timer.
        /// Bei Ablauf wird die Wiedergabe automatisch pausiert.
        /// </summary>
        /// <param name="duration">
        /// Zeitspanne bis zum automatischen Stopp. Null deaktiviert den Timer.
        /// </param>
        void SetSleepTimer(TimeSpan? duration);
    }
}
