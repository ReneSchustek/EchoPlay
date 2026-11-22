using System;
using System.Collections.Generic;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Der zentrale Wiedergabedienst. ViewModels und Tests hängen an dieser Schnittstelle statt an
    /// <see cref="PlayerService"/>, der den Mediaplayer von Windows braucht.
    /// </summary>
    public interface IPlayerService
    {
        /// <summary>
        /// Wird ausgelöst, wenn sich Abspielstatus, Track oder Position geändert haben.
        /// </summary>
        event EventHandler? StateChanged;

        /// <summary>
        /// Wird ausgelöst, wenn ein Wiedergabefehler auftritt.
        /// Die Nachricht ist für die Anzeige an den Nutzer geeignet.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "Der Typ ist nur öffentlich, weil WinUI-Bindungen öffentliche Typen verlangen; einen Abonnenten außerhalb der Anwendung gibt es nicht. Gleich gebaute Ereignisse in internen Klassen bleiben bei Action — ein EventArgs-Typ allein an den öffentlichen Stellen führte ein zweites Muster ein, ohne dass jemand davon profitiert.")]
        event EventHandler<string>? ErrorOccurred;

        /// <summary>Gibt an, ob gerade Wiedergabe aktiv ist.</summary>
        bool IsPlaying { get; }

        /// <summary>
        /// Titel des aktuell laufenden Tracks (Dateiname ohne Erweiterung).
        /// Null, wenn nichts spielt.
        /// </summary>
        string? CurrentTrackTitle { get; }

        /// <summary>
        /// Dateipfad des aktuell laufenden Tracks. Null, wenn nichts spielt.
        /// Über ihn findet eine angezeigte Wiedergabeliste ihre laufende Zeile. Der
        /// Anzeigename taugt dafür nicht, weil er aus der Kennzeichnung stammen kann.
        /// </summary>
        string? CurrentTrackPath { get; }

        /// <summary>
        /// Dateipfade der laufenden Wiedergabeliste, in Reihenfolge. Leer, wenn nichts spielt.
        /// Damit kann eine Seite die laufende Wiedergabe anzeigen, die sie nicht selbst
        /// gestartet hat, sonst bliebe sie leer, obwohl unten der Abspieler läuft.
        /// </summary>
        IReadOnlyList<string> CurrentTrackPaths { get; }

        /// <summary>Aktuelle Abspielposition.</summary>
        TimeSpan Position { get; }

        /// <summary>Gesamtdauer des aktuell laufenden Tracks.</summary>
        TimeSpan Duration { get; }

        /// <summary>
        /// Die Stelle in der ganzen Folge, über alle Spuren gerechnet. Ohne bekannte
        /// Spurdauern gleich <see cref="Position"/>.
        /// </summary>
        TimeSpan OverallPosition { get; }

        /// <summary>
        /// Die Gesamtdauer der Folge über alle Spuren; <see cref="TimeSpan.Zero"/>, wenn
        /// die Spurdauern nicht bekannt sind.
        /// </summary>
        TimeSpan OverallDuration { get; }

        /// <summary>
        /// Wiedergabegeschwindigkeit. 1.0 entspricht normaler Geschwindigkeit. Der Dienst reicht
        /// den Wert ungeprüft an den Mediaplayer von Windows weiter.
        /// </summary>
        double PlaybackRate { get; set; }

        /// <summary>
        /// Lautstärke der Wiedergabe, von 0,0 (still) bis 1,0 (voll). Werte außerhalb
        /// werden auf den Bereich gezogen.
        /// </summary>
        double Volume { get; set; }

        /// <summary>
        /// Ob die Wiedergabe stummgeschaltet ist. Die eingestellte Lautstärke bleibt dabei
        /// erhalten und gilt wieder, sobald die Stummschaltung endet.
        /// </summary>
        bool IsMuted { get; set; }

        /// <summary>
        /// Verbleibende Zeit des Einschlaf-Timers.
        /// Null, wenn kein Timer aktiv ist.
        /// </summary>
        TimeSpan? SleepTimerRemaining { get; }

        /// <summary>
        /// Startet die Wiedergabe einer Trackliste ab dem angegebenen Index.
        /// </summary>
        /// <param name="episodeId">ID der Episode für die PlaybackState-Persistenz.</param>
        /// <param name="trackPaths">Absolute Dateipfade der Audiotracks, in Reihenfolge.</param>
        /// <param name="startIndex">Index des ersten Tracks (0-basiert).</param>
        /// <param name="resumePosition">
        /// Stelle, ab der fortgesetzt wird. Sind Spurdauern angegeben, gilt sie für die
        /// ganze Folge und bestimmt damit auch die Spur; sonst für die Spur
        /// <paramref name="startIndex"/>.
        /// </param>
        /// <param name="trackDurations">
        /// Die Dauern der Spuren in derselben Reihenfolge wie <paramref name="trackPaths"/>.
        /// Ohne sie kennt der Dienst nur die laufende Spur, die Folge bleibt dann ohne
        /// Gesamtfortschritt.
        /// </param>
        void Play(
            Guid episodeId,
            IReadOnlyList<string> trackPaths,
            int startIndex = 0,
            TimeSpan resumePosition = default,
            IReadOnlyList<TimeSpan>? trackDurations = null);

        /// <summary>Pausiert die Wiedergabe.</summary>
        void Pause();

        /// <summary>
        /// Stoppt die Wiedergabe vollständig, speichert die aktuelle Position
        /// und leert die Playlist. Der MiniPlayer wird dadurch ausgeblendet,
        /// weil <see cref="CurrentTrackTitle"/> auf null gesetzt wird.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Die Regel schützt Aufrufer in Visual Basic vor Schlüsselwörtern; EchoPlay wird nur aus C# aufgerufen. Stop und Resume folgen dem Vokabular von Windows.Media.Playback.MediaPlayer.")]
        void Stop();

        /// <summary>Setzt eine pausierte Wiedergabe fort.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Die Regel schützt Aufrufer in Visual Basic vor Schlüsselwörtern; EchoPlay wird nur aus C# aufgerufen. Stop und Resume folgen dem Vokabular von Windows.Media.Playback.MediaPlayer.")]
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
        /// Zeitspanne bis zur automatischen Pause. Null deaktiviert den Timer.
        /// </param>
        void SetSleepTimer(TimeSpan? duration);
    }
}
