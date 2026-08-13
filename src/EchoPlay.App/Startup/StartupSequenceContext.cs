using EchoPlay.App.Services;
using EchoPlay.Core.Abstractions.Time;
using EchoPlay.Logger.Core;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace EchoPlay.App.Startup
{
    /// <summary>
    /// Bündelt die Dienste, die der Startlauf braucht.
    /// </summary>
    /// <remarks>
    /// Der Startlauf bekommt sie fertig aufgelöst statt eines Dienst-Verzeichnisses, aus
    /// dem er sich selbst bedient: Ein Typ, der den Container festhält, verbirgt seine
    /// Abhängigkeiten vor jedem, der ihn liest oder testet. Was scoped ist (Datenbank),
    /// kommt weiterhin über die Bereichs-Fabrik — der Startlauf öffnet dafür je Abschnitt
    /// einen eigenen Bereich.
    /// </remarks>
    /// <param name="ScopeFactory">Öffnet die Bereiche für datenbanknahe Dienste.</param>
    /// <param name="LoggerManager">Der Protokoll-Verwalter der Anwendung.</param>
    /// <param name="LanguageSwitchService">Setzt die Sprachvorgabe bei jedem Start neu.</param>
    /// <param name="Clock">Zeitquelle für den vermerkten Startzeitpunkt.</param>
    /// <param name="ProviderIdService">Trägt fehlende Anbieter-Kennungen nach.</param>
    /// <param name="ThemeService">Richtet das Farbschema vor dem ersten Zeichnen ein.</param>
    /// <param name="Validator">Führt die Prüfungen im Startbild aus.</param>
    /// <param name="UpdateInteraction">Bietet eine gefundene neue Version an.</param>
    /// <param name="PlayerService">Bekommt die gespeicherte Lautstärke beim Start.</param>
    internal sealed record StartupSequenceContext(
        IServiceScopeFactory ScopeFactory,
        LoggerManager LoggerManager,
        ILanguageSwitchService LanguageSwitchService,
        IClock Clock,
        BackgroundProviderIdService ProviderIdService,
        ThemeService ThemeService,
        IStartupValidator Validator,
        UpdateInteractionService UpdateInteraction,
        IPlayerService PlayerService)
    {
        /// <summary>
        /// Löst die Dienste aus dem fertig gebauten Container auf.
        /// </summary>
        /// <remarks>
        /// Die einzige Stelle, an der der Container als Ganzes gereicht wird — sie hält ihn
        /// nicht fest, sondern liest ihn einmal aus und gibt das Ergebnis weiter.
        /// </remarks>
        /// <param name="services">Die Dienstsammlung des Hosts.</param>
        /// <returns>Der gefüllte Kontext.</returns>
        // Der Container steht hier, weil das die DI-Root ist: gelesen wird einmal beim
        // Hochfahren, festgehalten wird er nicht.
        public static StartupSequenceContext From(IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(services);

            return new StartupSequenceContext(
                services.GetRequiredService<IServiceScopeFactory>(),
                services.GetRequiredService<LoggerManager>(),
                services.GetRequiredService<ILanguageSwitchService>(),
                services.GetRequiredService<IClock>(),
                services.GetRequiredService<BackgroundProviderIdService>(),
                services.GetRequiredService<ThemeService>(),
                services.GetRequiredService<IStartupValidator>(),
                services.GetRequiredService<UpdateInteractionService>(),
                services.GetRequiredService<IPlayerService>());
        }
    }
}
