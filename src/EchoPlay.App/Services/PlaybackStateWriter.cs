using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using EchoPlay.Logger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Schreibt den Hörstand einer Folge in die Datenbank — die Stelle, an der zuletzt
    /// aufgehört wurde, und wann das war.
    /// </summary>
    /// <remarks>
    /// Das läuft nebenher, während die Wiedergabe weitergeht: alle dreißig Sekunden, beim
    /// Anhalten und beim Beenden der Anwendung. Zwei Regeln machen es unauffällig — es darf
    /// nie zwei Schreibvorgänge gleichzeitig geben, und ein gescheiterter Schreibvorgang darf
    /// die Wiedergabe nicht stören. Beides gehört hierher und nicht in die Ansteuerung des
    /// Abspielgeräts.
    /// </remarks>
    public sealed class PlaybackStateWriter : IDisposable
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;
        private readonly IClock _clock;

        // Läuft schon ein Schreibvorgang, wird der nächste übersprungen statt eingereiht:
        // Der übernächste Durchlauf bringt ohnehin einen aktuelleren Stand.
        private readonly SemaphoreSlim _saveLock = new(1, 1);

        private bool _disposed;

        /// <summary>
        /// Richtet den Schreiber ein.
        /// </summary>
        /// <param name="scopeFactory">Für einen eigenen Datenbankbereich je Schreibvorgang.</param>
        /// <param name="logger">Protokollkanal für gescheiterte Versuche.</param>
        /// <param name="clock">Liefert den Zeitpunkt des letzten Hörens.</param>
        public PlaybackStateWriter(IServiceScopeFactory scopeFactory, ILogger logger, IClock clock)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _clock = clock;
        }

        /// <summary>
        /// Speichert die Stelle für eine Folge. Ohne Folgenkennung — etwa bei der Wiedergabe
        /// eines Ordners — passiert nichts.
        /// </summary>
        /// <param name="episodeId">Die Folge, für die gespeichert wird.</param>
        /// <param name="position">Die Stelle in der Folge, nicht in der laufenden Datei.</param>
        /// <returns>Der Task ist abgeschlossen, wenn geschrieben wurde oder der Versuch entfiel.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Persistenz der Abspielposition im Hintergrund: DbContext-/Concurrency-/Migration-Fehler dürfen die Wiedergabe nicht stören – bei Scheitern wird der Verlust geloggt und der nächste Autosave-Tick versucht es erneut.")]
        public async Task SaveAsync(Guid episodeId, TimeSpan position)
        {
            if (episodeId == Guid.Empty)
            {
                return;
            }

            // Läuft bereits ein Schreibvorgang, diesen Durchlauf überspringen.
            if (!await _saveLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IPlaybackStateDataService service = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

                // Das Sichern darf nicht an einem fremden Abbruchzeichen scheitern — die
                // Gleichzeitigkeit regelt die eigene Sperre.
                PlaybackState? existing = await service.GetByEpisodeIdAsync(episodeId, CancellationToken.None);

                if (existing is null)
                {
                    PlaybackState newState = new()
                    {
                        EpisodeId = episodeId,
                        LastPosition = position,
                        LastPlayedAt = _clock.UtcNow
                    };

                    await service.AddAsync(newState, CancellationToken.None);
                }
                else
                {
                    existing.LastPosition = position;
                    existing.LastPlayedAt = _clock.UtcNow;
                    await service.UpdateAsync(existing, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning("Wiedergabestatus konnte nicht gespeichert werden: {Reason}", ex.Message);
            }
            finally
            {
                _ = _saveLock.Release();
            }
        }

        /// <summary>Gibt die Sperre frei.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _saveLock.Dispose();
            _disposed = true;
        }
    }
}
