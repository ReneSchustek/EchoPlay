using EchoPlay.App.Services;
using EchoPlay.Core.Abstractions.Time;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace EchoPlay.App.Composition
{
    /// <summary>
    /// Registriert die Dienste der Anwendungsschicht: Oberflächen-Dienste wie Farbschema,
    /// Navigation und Dialoge, die Hintergrundarbeiter rund um Cover und Anbieter-Kennungen
    /// sowie die Koordinatoren, die den Ansichtsmodellen Fachlogik abnehmen.
    /// </summary>
    internal static class AppServiceRegistration
    {
        /// <summary>Mindestabstand zwischen zwei Anfragen je Gegenstelle.</summary>
        private static readonly Dictionary<string, TimeSpan> HostRateLimits = new()
        {
            ["musicbrainz.org"] = TimeSpan.FromSeconds(1),
            ["coverartarchive.org"] = TimeSpan.FromSeconds(1),
            ["itunes.apple.com"] = TimeSpan.FromMilliseconds(1500),
            ["api.discogs.com"] = TimeSpan.FromSeconds(1),
        };

        /// <summary>Mindestabstand für Gegenstellen, die nur Bilder ausliefern.</summary>
        /// <remarks>
        /// Ein Bildabruf an einem Auslieferungsnetz ist kein API-Aufruf: Es gibt dort kein
        /// Kontingent zu schonen, und die Cover einer Trefferseite sollen nebeneinander laden
        /// statt hintereinander. Mit dem Standardabstand von einer Sekunde erschien das
        /// fünfzehnte Cover erst nach fünfzehn Sekunden. Die Rechnernamen wechseln
        /// (<c>is1-ssl</c> bis <c>is5-ssl</c>), deshalb die Endung statt des vollen Namens.
        /// </remarks>
        private static readonly Dictionary<string, TimeSpan> ImageHostRateLimits = new()
        {
            [".mzstatic.com"] = TimeSpan.FromMilliseconds(50),
            [".scdn.co"] = TimeSpan.FromMilliseconds(50),
        };

        /// <summary>
        /// Registriert alle Dienste der Anwendungsschicht.
        /// </summary>
        /// <param name="services">Die zu befüllende Dienstsammlung.</param>
        /// <returns>Dieselbe Dienstsammlung, damit Aufrufe verkettet werden können.</returns>
        public static IServiceCollection AddEchoPlayAppServices(this IServiceCollection services)
        {
            AddShellServices(services);
            AddPlaybackAndImport(services);
            AddCoverServices(services);
            AddCoordinators(services);
            AddUpdateServices(services);
            return services;
        }

        /// <summary>
        /// Registriert den <see cref="CoverService"/> als Singleton und bildet
        /// <see cref="ICoverService"/> auf DIESELBE konkrete Instanz ab.
        /// </summary>
        /// <remarks>
        /// Die Fabrik löst bewusst den KONKRETEN <see cref="CoverService"/> auf, nicht
        /// <see cref="ICoverService"/> selbst — eine Selbst-Auflösung würde endlos
        /// rekursieren (der Container erkennt nur Zyklen über Konstruktoren, nicht über
        /// Fabriken) und den Start einfrieren. Bewusst als eigene Methode, damit ein Test
        /// genau diese Verdrahtung auflösen und gegen den Rückfall absichern kann.
        /// Gleiches Muster wie bei Farbschema-, Navigations- und Wiedergabedienst.
        /// </remarks>
        /// <param name="services">Die zu befüllende Dienstsammlung.</param>
        public static void RegisterCoverService(IServiceCollection services)
        {
            _ = services.AddSingleton<CoverService>();
            _ = services.AddSingleton<ICoverService>(provider => provider.GetRequiredService<CoverService>());
        }

        /// <summary>
        /// Dienste rund um Fenster, Navigation, Sprache und Dialoge. Durchweg Singletons,
        /// weil ihr Zustand genau einmal existieren darf.
        /// </summary>
        private static void AddShellServices(IServiceCollection services)
        {
            // Der Zustand (aktives Farbschema) darf nur einmal existieren.
            _ = services.AddSingleton<IThemeService, ThemeService>();
            _ = services.AddSingleton<ThemeService>(provider =>
                (ThemeService)provider.GetRequiredService<IThemeService>());

            // Einzige Stelle, an der in Produktion die Systemzeit gelesen wird. Tests
            // setzen eine feste Zeitquelle ein und werden damit reproduzierbar.
            _ = services.AddSingleton<IClock, SystemClock>();
            _ = services.AddSingleton<IHostRateLimiter>(_ => new SemaphoreHostRateLimiter(
                HostRateLimits, defaultInterval: null, suffixIntervals: ImageHostRateLimits));

            // Hängt die Ratenbremse in die HTTP-Kette ein. Muss transient sein, damit die
            // Client-Fabrik je Client eine eigene Instanz erzeugt.
            _ = services.AddTransient<RateLimitMessageHandler>();

            // Überlebt die Navigation und benachrichtigt das neue Ansichtsmodell nach der Rückkehr.
            _ = services.AddSingleton<IScanEventService, ScanEventService>();

            // Der Inhaltsrahmen existiert genau einmal je Anwendung; das Hauptfenster
            // meldet ihn nach dem Aufbau an.
            _ = services.AddSingleton<INavigationService, NavigationService>();
            _ = services.AddSingleton<NavigationService>(provider =>
                (NavigationService)provider.GetRequiredService<INavigationService>());

            // Kapselt das Umschalten der Überwachung samt Zwischenspeicher der
            // Neuerscheinungen. Beide Mediathek-Ansichtsmodelle nutzen denselben Dienst,
            // statt die Logik im Code hinter der Seite zu verdoppeln.
            _ = services.AddSingleton<IWatchToggleService, WatchToggleService>();

            // Meldet Änderungen am Zwischenspeicher an die bereits gezeichnete Startseite.
            // Singleton, weil Auslöser (kurzlebiger Bereich) und Abonnent (kurzlebiges
            // Ansichtsmodell) sich sonst nicht finden.
            _ = services.AddSingleton<INewReleaseEventService, NewReleaseEventService>();

            // Die Ressourcen-Instanz ist threadsicher und teuer zu erzeugen.
            _ = services.AddSingleton<ILocalizationService, LocalizationService>();

            // Kennt die dauerhaft ausgeblendeten Dialoge und hält sie im Speicher. Muss vor
            // den beiden Dialogdiensten stehen, die ihn bei jedem Hinweis befragen.
            _ = services.AddSingleton<IDialogSuppressionService, DialogSuppressionService>();

            _ = services.AddSingleton<IErrorDialogService, ErrorDialogService>();
            _ = services.AddSingleton<ErrorDialogService>(provider =>
                (ErrorDialogService)provider.GetRequiredService<IErrorDialogService>());
            _ = services.AddSingleton<IConfirmationDialogService, ConfirmationDialogService>();

            // Prüft den Offline-Modus und schaltet die Statusleiste vorübergehend auf online.
            _ = services.AddSingleton<IOnlineAccessGuard, OnlineAccessGuard>();

            // Kapselt die Offline-/Nur-Online-Prüfung beim Betreten einer Seite, damit die
            // Ansichtsmodelle sie nicht jeweils selbst mitführen.
            _ = services.AddSingleton<IPageModeGuard, PageModeGuard>();

            // Steuert den Fortschrittsbalken im Symbol der Taskleiste.
            _ = services.AddSingleton<TaskbarProgressService>();

            // Zentrale Auswahl von Ordnern und Dateien; kapselt die Fensterkennung, die
            // WinUI 3 dafür braucht.
            _ = services.AddSingleton<IFilePickerService, FilePickerService>();

            // Sprachwechsel an einer Stelle: Ablage, Sprachvorgabe und Neustart. Wird schon
            // im Startpfad gebraucht, deshalb Singleton.
            _ = services.AddSingleton<IProcessLauncher, ProcessLauncher>();
            _ = services.AddSingleton<ILanguageSwitchService, LanguageSwitchService>();

            // Such- und Filterkriterien der Seiten. Die Seiten-Ansichtsmodelle sind
            // kurzlebig; ohne diese Ablage stünde jede Liste nach einem Ausflug in eine
            // Detailansicht wieder ungefiltert da.
            _ = services.AddSingleton<FilterStateStore>();

            // Führt alle Prüfungen im Startbild aus.
            _ = services.AddSingleton<IStartupValidator, StartupValidator>();
        }

        /// <summary>
        /// Wiedergabe, Abgleich und Import. Alle drei halten eigenen Zustand und legen
        /// sich für Datenbankzugriffe intern einen eigenen Bereich an.
        /// </summary>
        private static void AddPlaybackAndImport(IServiceCollection services)
        {
            // Es darf immer nur eine Wiedergabeinstanz geben.
            _ = services.AddSingleton<IPlayerService, PlayerService>();
            _ = services.AddSingleton<PlayerService>(provider =>
                (PlayerService)provider.GetRequiredService<IPlayerService>());

            _ = services.AddSingleton<ISyncService, SyncService>();
            _ = services.AddSingleton<SyncService>(provider =>
                (SyncService)provider.GetRequiredService<ISyncService>());

            _ = services.AddSingleton<ImportService>();
        }

        /// <summary>
        /// Cover-Abruf und die beiden Hintergrundarbeiter, die fehlende Bilder und
        /// Anbieter-Kennungen nachtragen.
        /// </summary>
        private static void AddCoverServices(IServiceCollection services)
        {
            // Einzige Stelle, die Cover-Bytes per HTTP holt.
            _ = services.AddSingleton<ICoverDownloader, CoverDownloader>();

            // Eigener Dienst statt Teil des Imports, damit beim Laden des Import-Typs nicht
            // die LocalLibrary-Baugruppe mitgeladen wird.
            _ = services.AddSingleton<EpisodeCoverCacheService>();

            RegisterCoverService(services);

            _ = services.AddSingleton(new BackgroundCoverServiceOptions());
            _ = services.AddSingleton<BackgroundCoverService>();
            _ = services.AddSingleton<BackgroundProviderIdService>();
        }

        /// <summary>
        /// Koordinatoren, die den Ansichtsmodellen Abläufe abnehmen, welche über mehrere
        /// Dienste hinweg laufen.
        /// </summary>
        private static void AddCoordinators(IServiceCollection services)
        {
            // Kapselt Einstellungs-Abfrage, den Umbau-Dienst der lokalen Mediathek und die
            // Abbildung auf die Anzeige.
            _ = services.AddSingleton<IFolderRestructureCoordinator, FolderRestructureCoordinator>();

            // Kapselt Dateisystem-Analyse, den Abgleich gegen iTunes und die Statusleiste
            // für die Prüfung auf fehlende Folgen.
            _ = services.AddSingleton<IMissingEpisodesCoordinator, MissingEpisodesCoordinator>();

            // Kapselt Cover-Suche, Rückfrage beim Überschreiben, Download, Ablage in der
            // Cover-Tabelle, optionales Speichern als Datei und die Auffrischung der Kachel.
            _ = services.AddSingleton<IEpisodeCoverCoordinator, EpisodeCoverCoordinator>();

            // Verbindungstest und Protokollansicht der Einstellungen als eigene
            // Koordinatoren, damit das Einstellungs-Ansichtsmodell sie nicht selbst trägt.
            _ = services.AddSingleton<IConnectionTestCoordinator, ConnectionTestCoordinator>();
            _ = services.AddSingleton<ILogViewerCoordinator>(provider => new LogViewerCoordinator(
                provider.GetRequiredService<EchoPlay.Logger.Core.LoggerManager>(),
                provider.GetService<EchoPlay.Logger.Sinks.MemorySink>()));

            // Kapselt die MusicBrainz-Abfrage samt Aufbau der Suchanfrage und Bewertung
            // der Treffer für den Tag-Manager.
            _ = services.AddSingleton<ITagLookupCoordinator, TagLookupCoordinator>();
        }

        /// <summary>
        /// Prüfung auf neue Versionen und der Download des Installationspakets. Der Start
        /// des Installationsprogramms liegt hinter einer eigenen Schnittstelle, damit Tests
        /// den Download-Pfad vollständig durchlaufen können, ohne eine Datei zu starten.
        /// </summary>
        private static void AddUpdateServices(IServiceCollection services)
        {
            _ = services.AddSingleton<UpdateCheckService>();
            _ = services.AddSingleton<IInstallerLauncher, InstallerLauncher>();
            _ = services.AddSingleton<UpdateDownloadService>();
            _ = services.AddSingleton<UpdateInteractionService>();
        }
    }
}
