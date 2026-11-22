using Windows.Media.Playback;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Die Teile der Wiedergabe-Engine, die <see cref="PlayerService"/> bedient. Als eigene
    /// Schnittstelle, damit sich Zustand und Fehlerwege des Dienstes ohne die Medienwiedergabe
    /// des Betriebssystems prüfen lassen: Ein Windows Server ohne sie weist den Start ab, und
    /// der Dienst setzt dann zu Recht alles zurück.
    /// </summary>
    internal interface IMediaEngine : IDisposable
    {
        /// <summary>Meldet jeden Wechsel zwischen Abspielen, Pause, Puffern und Stillstand.</summary>
        event EventHandler? PlaybackStateChanged;

        /// <summary>Meldet eine Spur, die die Engine nicht öffnen oder abspielen kann.</summary>
        event EventHandler<MediaEngineFailure>? Failed;

        /// <summary>Der Zustand der Wiedergabe.</summary>
        MediaPlaybackState PlaybackState { get; }

        /// <summary>Die Stelle in der laufenden Spur.</summary>
        TimeSpan Position { get; set; }

        /// <summary>Die Länge der laufenden Spur, solange die Engine sie kennt; sonst null.</summary>
        TimeSpan NaturalDuration { get; }

        /// <summary>Die Abspielgeschwindigkeit, 1,0 ist normal.</summary>
        double PlaybackRate { get; set; }

        /// <summary>Die Lautstärke von 0,0 bis 1,0.</summary>
        double Volume { get; set; }

        /// <summary>Ob der Ton stumm geschaltet ist.</summary>
        bool IsMuted { get; set; }

        /// <summary>Beginnt oder setzt die Wiedergabe der Abspielliste fort.</summary>
        void Play();

        /// <summary>Hält die Wiedergabe an.</summary>
        void Pause();
    }

    /// <summary>Was die Engine über eine fehlgeschlagene Spur meldet.</summary>
    /// <param name="Message">Die Meldung des Systems, falls es eine liefert.</param>
    /// <param name="Error">Die Fehlerart des Systems.</param>
    internal readonly record struct MediaEngineFailure(string? Message, MediaPlayerError Error);
}
