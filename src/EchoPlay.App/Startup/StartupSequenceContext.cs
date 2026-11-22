using EchoPlay.App.Services;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Logger.Core;
using Microsoft.Extensions.DependencyInjection;

namespace EchoPlay.App.Startup
{
    /// <summary>
    /// Bündelt die Dienste, die der Startlauf braucht.
    /// </summary>
    /// <remarks>
    /// Der Startlauf bekommt sie fertig aufgelöst statt eines Dienst-Verzeichnisses, aus
    /// dem er sich selbst bedient: Ein Typ, der den Container festhält, verbirgt seine
    /// Abhängigkeiten vor jedem, der ihn liest oder testet. Was scoped ist (Datenbank),
    /// kommt weiterhin über die Bereichs-Fabrik; der Startlauf öffnet dafür je Abschnitt
    /// einen eigenen Bereich.
    /// </remarks>
    internal sealed record StartupSequenceContext
    {
        /// <summary>Öffnet die Bereiche für datenbanknahe Dienste.</summary>
        public required IServiceScopeFactory ScopeFactory { get; init; }

        /// <summary>Der Protokoll-Verwalter der Anwendung.</summary>
        public required LoggerManager LoggerManager { get; init; }

        /// <summary>Setzt die Sprachvorgabe bei jedem Start neu.</summary>
        public required ILanguageSwitchService LanguageSwitchService { get; init; }

        /// <summary>Zeitquelle für den vermerkten Startzeitpunkt.</summary>
        public required IClock Clock { get; init; }

        /// <summary>Trägt fehlende Anbieter-Kennungen nach.</summary>
        public required BackgroundProviderIdService ProviderIdService { get; init; }

        /// <summary>Baut den Veröffentlichungskalender im Hintergrund auf.</summary>
        public required ReleaseCalendarRefreshService ReleaseCalendarService { get; init; }

        /// <summary>Gleicht im Hintergrund über den gemeinsamen Ordner ab.</summary>
        public required DeviceReplicationService ReplicationService { get; init; }

        /// <summary>Richtet das Farbschema vor dem ersten Zeichnen ein.</summary>
        public required ThemeService ThemeService { get; init; }

        /// <summary>Führt die Prüfungen im Startbild aus.</summary>
        public required IStartupValidator Validator { get; init; }

        /// <summary>Bietet eine gefundene neue Version an.</summary>
        public required UpdateInteractionService UpdateInteraction { get; init; }

        /// <summary>Bekommt die gespeicherte Lautstärke beim Start.</summary>
        public required IPlayerService PlayerService { get; init; }

        /// <summary>
        /// Löst die Dienste aus dem fertig gebauten Container auf.
        /// </summary>
        /// <remarks>
        /// Die einzige Stelle, an der der Container als Ganzes gereicht wird. Sie hält ihn
        /// nicht fest, sondern liest ihn einmal aus und gibt das Ergebnis weiter.
        /// </remarks>
        /// <param name="services">Die Dienstsammlung des Hosts.</param>
        /// <returns>Der gefüllte Kontext.</returns>
        public static StartupSequenceContext From(IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(services);

            return new StartupSequenceContext
            {
                ScopeFactory = services.GetRequiredService<IServiceScopeFactory>(),
                LoggerManager = services.GetRequiredService<LoggerManager>(),
                LanguageSwitchService = services.GetRequiredService<ILanguageSwitchService>(),
                Clock = services.GetRequiredService<IClock>(),
                ProviderIdService = services.GetRequiredService<BackgroundProviderIdService>(),
                ReleaseCalendarService = services.GetRequiredService<ReleaseCalendarRefreshService>(),
                ReplicationService = services.GetRequiredService<DeviceReplicationService>(),
                ThemeService = services.GetRequiredService<ThemeService>(),
                Validator = services.GetRequiredService<IStartupValidator>(),
                UpdateInteraction = services.GetRequiredService<UpdateInteractionService>(),
                PlayerService = services.GetRequiredService<IPlayerService>(),
            };
        }
    }
}
