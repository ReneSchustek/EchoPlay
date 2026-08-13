using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EchoPlay.App.Helpers;
using EchoPlay.Data.Entities.Library;
using EchoPlay.Data.Entities.Playback;
using EchoPlay.Data.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Services
{
    /// <summary>
    /// Startet die Wiedergabe einer lokalen Episode: lädt die Tracks, berechnet die
    /// Fortsetzungsposition aus dem gespeicherten Wiedergabestand und übergibt sie an den
    /// <see cref="IPlayerService"/>. Bündelt die sonst in mehreren ViewModels wiederholte Sequenz.
    /// </summary>
    internal static class PlaybackLauncher
    {
        /// <summary>
        /// Lädt die Tracks der Episode und startet die Wiedergabe – fortgesetzt an der zuletzt
        /// gespeicherten Position, sofern dort noch etwas offen ist.
        /// </summary>
        /// <param name="scopeFactory">Die Scope-Factory des aufrufenden ViewModels.</param>
        /// <param name="playerService">Der Player-Service, der die Wiedergabe startet.</param>
        /// <param name="episodeId">Die ID der abzuspielenden Episode.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Der Task ist abgeschlossen, wenn die Wiedergabe angestoßen ist — nicht erst am Ende der Episode.</returns>
        public static async Task PlayEpisodeAsync(
            IServiceScopeFactory scopeFactory,
            IPlayerService playerService,
            Guid episodeId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(scopeFactory);
            ArgumentNullException.ThrowIfNull(playerService);

            using IServiceScope scope = scopeFactory.CreateScope();
            ILocalTrackDataService trackService = scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();
            IPlaybackStateDataService stateService = scope.ServiceProvider.GetRequiredService<IPlaybackStateDataService>();

            IReadOnlyList<LocalTrack> tracks = await trackService.GetByEpisodeIdAsync(episodeId, cancellationToken);

            if (tracks.Count == 0)
            {
                return;
            }

            PlaybackState? savedState = await stateService.GetByEpisodeIdAsync(episodeId, cancellationToken);

            List<string> paths = new(tracks.Count);
            List<TimeSpan> durations = new(tracks.Count);
            foreach (LocalTrack track in tracks)
            {
                paths.Add(track.FilePath);
                durations.Add(track.Duration);
            }

            // Fortsetzen, wo etwas offen ist — sonst von vorn. Der Hörstatus entscheidet das
            // nicht: Wer eine gehörte Folge erneut angefangen und in der Mitte aufgehört hat,
            // will genau dort weiter. Eine durchgehörte Folge steht dagegen mit ihrer Stelle
            // am Ende und beginnt deshalb wieder von vorn.
            //
            // Die Gesamtdauer kommt aus den Spuren, nicht aus dem Feld der Folge: Rund die
            // Hälfte des Bestands führt dort keine Dauer, die Spuren dagegen schon.
            EpisodeTimeline timeline = new(durations);
            TimeSpan resumePosition =
                savedState is not null
                && EpisodeTimeline.HasOpenPosition(savedState.LastPosition, timeline.TotalDuration)
                    ? savedState.LastPosition
                    : TimeSpan.Zero;

            // Die Spurdauern entscheiden mit, in welcher Spur die Wiedergabe einsetzt: Die
            // gespeicherte Stelle gilt für die ganze Folge, nicht für eine einzelne Datei.
            playerService.Play(episodeId, paths, startIndex: 0, resumePosition: resumePosition, trackDurations: durations);
        }

        /// <summary>
        /// Setzt die Regel „erst lokal, sonst online" um: Liegt die Folge als Datei vor, wird
        /// sie abgespielt; sonst geht sie beim Anbieter auf. Abgewiesen wird nur, wenn es
        /// beides nicht gibt.
        /// </summary>
        /// <param name="scopeFactory">Die Scope-Factory des aufrufenden ViewModels.</param>
        /// <param name="playerService">Der Player-Service, der die Wiedergabe startet.</param>
        /// <param name="episodeId">Die ID der Folge.</param>
        /// <param name="seriesTitle">Serientitel — dient der Suche beim Anbieter, wenn keine Album-Kennung vorliegt.</param>
        /// <param name="episodeTitle">Folgentitel, ebenfalls für die Suche.</param>
        /// <param name="spotifyAlbumId">Spotify-Album-Kennung, sofern der Aufrufer sie kennt.</param>
        /// <param name="appleMusicAlbumId">Apple-Music-Album-Kennung, sofern der Aufrufer sie kennt.</param>
        /// <param name="cancellationToken">Abbruch-Token der umgebenden Operation.</param>
        /// <returns>Was tatsächlich geschehen ist.</returns>
        public static async Task<EpisodeLaunchResult> PlayOrOpenProviderAsync(
            IServiceScopeFactory scopeFactory,
            IPlayerService playerService,
            Guid episodeId,
            string seriesTitle,
            string episodeTitle,
            string? spotifyAlbumId = null,
            string? appleMusicAlbumId = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(scopeFactory);
            ArgumentNullException.ThrowIfNull(playerService);

            using IServiceScope scope = scopeFactory.CreateScope();
            ILocalTrackDataService trackService = scope.ServiceProvider.GetRequiredService<ILocalTrackDataService>();

            IReadOnlyList<LocalTrack> tracks = await trackService.GetByEpisodeIdAsync(episodeId, cancellationToken);

            if (tracks.Count > 0)
            {
                await PlayEpisodeAsync(scopeFactory, playerService, episodeId, cancellationToken).ConfigureAwait(false);
                return EpisodeLaunchResult.PlayedLocally;
            }

            // Die Kennungen kommen vom Aufrufer, nicht aus der Datenbank: Eine Neuerscheinung
            // stammt vom Anbieter und ist noch keine gespeicherte Folge — ihre Id steht in
            // keiner Tabelle (gemessen am 12.08.2026 über alle Id-Spalten). Ein Nachladen fand
            // deshalb nie etwas, und jeder Klick fiel auf die Suche zurück.
            // Wo eine echte Folge vorliegt, füllt der Aufrufer die Kennungen aus ihrem Datensatz.

            // Ohne diese Spur ist von außen nicht zu sehen, welcher Zweig gegriffen hat: Der
            // Sprung zum Anbieter verlässt die Anwendung, und ein stiller Fehlschlag sieht
            // genauso aus wie ein Erfolg.
            EchoPlay.Logger.Abstractions.ILogger log = scope.ServiceProvider
                .GetRequiredService<EchoPlay.Logger.Abstractions.ILoggerFactory>()
                .CreateLogger("PlaybackLauncher");

            log.Info($"Folge {episodeId} ohne lokale Datei. "
                + $"Spotify-Kennung {(string.IsNullOrWhiteSpace(spotifyAlbumId) ? "fehlt" : "vorhanden")}, "
                + $"Apple-Kennung {(string.IsNullOrWhiteSpace(appleMusicAlbumId) ? "fehlt" : "vorhanden")}.");

            // Reihenfolge: Erst die genauen Treffer beider Anbieter, dann erst die Suche.
            // Andernfalls verdrängt eine unscharfe Spotify-Suche die exakte Apple-Kennung —
            // gemessen an „Fünf Freunde Junior, Folge 17": dort gibt es nur die Apple-Kennung.
            if (TryOpenSpotifyAlbum(spotifyAlbumId))
            {
                log.Info($"Folge {episodeId} in Spotify geöffnet (Album).");
                return EpisodeLaunchResult.OpenedAtProvider;
            }

            if (TryOpenAppleMusic(appleMusicAlbumId))
            {
                log.Info($"Folge {episodeId} in Apple Music geöffnet (Album).");
                return EpisodeLaunchResult.OpenedAtProvider;
            }

            if (TryOpenSpotifySearch(seriesTitle, episodeTitle))
            {
                log.Info($"Folge {episodeId} als Spotify-Suche geöffnet — keine Album-Kennung vorhanden.");
                return EpisodeLaunchResult.OpenedAtProvider;
            }

            log.Warning($"Folge {episodeId} konnte weder abgespielt noch beim Anbieter geöffnet werden.");
            return EpisodeLaunchResult.NothingToPlay;
        }

        /// <summary>
        /// Ermittelt die Ziele beim Anbieter, ohne etwas zu öffnen. Getrennt vom Öffnen, damit
        /// die Auswahl prüfbar ist — ein Test darf keinen Browser und keine fremde Anwendung
        /// starten.
        /// </summary>
        /// <param name="spotifyAlbumId">Spotify-Album-Kennung der Folge, sofern bekannt.</param>
        /// <param name="seriesTitle">Serientitel für die Suche ohne Kennung.</param>
        /// <param name="episodeTitle">Folgentitel für die Suche ohne Kennung.</param>
        /// <param name="appUri">Sprungziel der Spotify-Anwendung, sonst <see langword="null"/>.</param>
        /// <param name="webUrl">Sprungziel im Browser, sonst <see langword="null"/>.</param>
        /// <returns><see langword="true"/>, wenn mindestens ein Ziel entstanden ist.</returns>
        internal static bool TryBuildSpotifyTargets(
            string? spotifyAlbumId,
            string seriesTitle,
            string episodeTitle,
            out string? appUri,
            out string? webUrl)
        {
            _ = SpotifyAlbumLink.TryBuildAppUri(spotifyAlbumId, out appUri)
                || SpotifyAlbumLink.TrySearchAppUri([seriesTitle, episodeTitle], out appUri);

            _ = SpotifyAlbumLink.TryBuild(spotifyAlbumId, out webUrl)
                || SpotifyAlbumLink.TrySearch([seriesTitle, episodeTitle], out webUrl);

            return appUri is not null || webUrl is not null;
        }

        private static bool TryOpenSpotifyAlbum(string? albumId)
        {
            if (!SpotifyAlbumLink.TryBuild(albumId, out string? webUrl))
            {
                return false;
            }

            // Erst die Anwendung: Dort ist der Nutzer angemeldet, im Browser meist nicht.
            _ = SpotifyAlbumLink.TryBuildAppUri(albumId, out string? appUri);
            return SafeUrlLauncher.TryOpenAppLink(appUri, "spotify") || SafeUrlLauncher.TryOpenInBrowser(webUrl);
        }

        private static bool TryOpenSpotifySearch(string seriesTitle, string episodeTitle)
        {
            if (!SpotifyAlbumLink.TrySearch([seriesTitle, episodeTitle], out string? webUrl))
            {
                return false;
            }

            _ = SpotifyAlbumLink.TrySearchAppUri([seriesTitle, episodeTitle], out string? appUri);
            return SafeUrlLauncher.TryOpenAppLink(appUri, "spotify") || SafeUrlLauncher.TryOpenInBrowser(webUrl);
        }

        private static bool TryOpenAppleMusic(string? albumId)
        {
            return AppleMusicAlbumLink.TryBuild(albumId, out string? url)
                && SafeUrlLauncher.TryOpenInBrowser(url);
        }
    }

    /// <summary>
    /// Ergebnis eines Klicks auf eine Folge — welcher Zweig der Regel „erst lokal, sonst
    /// online" gegriffen hat.
    /// </summary>
    internal enum EpisodeLaunchResult
    {
        /// <summary>Die Folge lag als Datei vor und wird abgespielt.</summary>
        PlayedLocally,

        /// <summary>Keine Datei vorhanden; die Folge ist beim Anbieter aufgegangen.</summary>
        OpenedAtProvider,

        /// <summary>Weder Datei noch Anbieter-Verweis — hier bleibt nur der Hinweis.</summary>
        NothingToPlay
    }
}
